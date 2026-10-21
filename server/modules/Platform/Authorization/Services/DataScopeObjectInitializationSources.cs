using System.Reflection;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>初始化来源只做零SQL事实核对，父读取/编辑与候选创建仍由同一正式执行负责。</summary>
public sealed class DataScopeObjectInitializationSources(DataScopeCatalog catalog, CanonicalModulePlan plan, IDataExecutionAccess execution,
    IEnumerable<DataScopeObjectInitializationBinding> declared,
    IEnumerable<DataScopeObjectInitializationCreatedParentsBinding> createdParents,
    ICP.Platform.Persistence.Abstractions.IPlatformTransactionExecutor transactions, EfPlatformDbContext db)
{
    /// <summary>在首次Flush前捕获当前真实Added行；证明只属于本Sources实例和当前事务，不保存允许结果。</summary>
    public DataScopeObjectCreationProof CaptureCreatedObjects(string resourceKey, IReadOnlyList<DataExecutionWriteFacts> objects)
    {
        var resource = catalog.Resolve(resourceKey);
        var transaction = db.Database.CurrentTransaction;
        if (!transactions.IsActive || transaction is null || objects is null || objects.Count == 0) throw Invalid();
        var key = db.Model.FindEntityType(resource.ModelName)?.FindPrimaryKey();
        if (key?.Properties.Count != 1 || key.Properties[0].Name != resource.IdField) throw Invalid();
        var entries = new Dictionary<string, EntityEntry<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        var detect = db.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var local = db.Set<Dictionary<string, object?>>(resource.ModelName).Local;
            foreach (var facts in objects)
            {
                if (!MatchesIdentity(resource, facts)) throw Invalid();
                var entry = local.FindEntry(facts.ObjectId);
                if (entry is null || entry.State != EntityState.Added || !MatchesValues(entry.Entity, facts.Values)
                    || !entries.TryAdd(facts.ObjectId, entry)) throw Invalid();
            }
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = detect; }
        return new CreationProof(this, transaction, resourceKey, entries);
    }

    /// <summary>在任何配置锁、读取或克隆前核验真实父集合、新对象和有限调用者来源。</summary>
    public void Validate(string resourceKey, IReadOnlyList<string> parents, string newObjectId, DataScopeObjectInitializationSource source)
    {
        var resource = catalog.Resolve(resourceKey);
        if (source is null || source.Target is null || source.Target.Values is null
            || source.Target.ResourceKey != resourceKey || source.Target.ObjectId != newObjectId
            || !source.Target.Values.TryGetValue(resource.IdField, out var actualId) || actualId is not string id || id != newObjectId
            || source.ParentObjectIds is null || source.ParentObjectIds.Any(string.IsNullOrWhiteSpace)
            || !parents.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(source.ParentObjectIds))
            throw Invalid();
        switch (source)
        {
            case DataScopeObjectInitializationSource.Created current when current.Proof is not null:
                if (current.Caller is not null) throw Invalid();
                ValidateCapturedObjects(resource, current.Proof, [current.Target]);
                break;
            case DataScopeObjectInitializationSource.Created current:
                ValidateAddedTarget(resource, newObjectId, current);
                break;
            case DataScopeObjectInitializationSource.CreatedFromCreatedParents graph:
                if (graph.Caller is not null || graph.Parents is null || graph.Parents.Count != parents.Count || parents.Count == 0
                    || graph.Parents.Any(parent => parent is null || parent.ObjectId == newObjectId)
                    || graph.Parents.Select(parent => parent.ObjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != parents.Count
                    || !parents.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(graph.Parents.Select(parent => parent.ObjectId))) throw Invalid();
                ValidateCapturedObjects(resource, graph.Proof, [graph.Target, .. graph.Parents]);
                break;
            case DataScopeObjectInitializationSource.Declared current when current.Caller is not null:
                ValidateDeclared(resource, parents.Count, current);
                break;
            case DataScopeObjectInitializationSource.DeclaredFromCreatedParents graph when graph.Caller is not null:
                ValidateCreatedParents(resource, parents, newObjectId, graph);
                break;
            default: throw Invalid();
        }
    }

    // 只接受本实现实际发行的同scope/事务证明，随后以原Entry和当前完整事实验证，不读取数据库。
    private void ValidateCapturedObjects(DataScopeResource resource, DataScopeObjectCreationProof proof, IReadOnlyList<DataExecutionWriteFacts> objects)
    {
        if (proof is not CreationProof captured || !ReferenceEquals(captured.Owner, this) || captured.ResourceKey != resource.ResourceKey
            || !transactions.IsActive || !ReferenceEquals(captured.Transaction, db.Database.CurrentTransaction)) throw Invalid();
        var detect = db.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var local = db.Set<Dictionary<string, object?>>(resource.ModelName).Local;
            foreach (var facts in objects)
            {
                if (!MatchesIdentity(resource, facts) || !captured.Entries.TryGetValue(facts.ObjectId, out var original)
                    || !ReferenceEquals(original.Context, db) || original.Metadata.Name != resource.ModelName
                    || original.State is EntityState.Detached or EntityState.Deleted) throw Invalid();
                var current = local.FindEntry(facts.ObjectId);
                if (current is null || !ReferenceEquals(current.Entity, original.Entity) || !MatchesValues(current.Entity, facts.Values)) throw Invalid();
            }
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = detect; }
    }

    // 事实必须使用已登记资源的真实主键；调用者不能以另一资源或第二个权限专用ID替代。
    private static bool MatchesIdentity(DataScopeResource resource, DataExecutionWriteFacts? facts) => facts is not null
        && facts.ResourceKey == resource.ResourceKey && !string.IsNullOrWhiteSpace(facts.ObjectId) && facts.Values is not null
        && facts.Values.TryGetValue(resource.IdField, out var key) && key is string id && id == facts.ObjectId;

    // 允许原创建行在Flush后有正常字段变化，但本次提交的事实必须完整等于同一当前tracked实体。
    private static bool MatchesValues(IReadOnlyDictionary<string, object?> entity, IReadOnlyDictionary<string, object?> values) =>
        entity.Count == values.Count && values.All(value => entity.TryGetValue(value.Key, out var actual) && Equals(actual, value.Value));

    // 私有sealed类型与owner引用共同确定实际发行方；公开基类型、相同字段或自制派生对象均不成立。
    private sealed class CreationProof(DataScopeObjectInitializationSources owner, IDbContextTransaction transaction, string resourceKey,
        IReadOnlyDictionary<string, EntityEntry<Dictionary<string, object?>>> entries) : DataScopeObjectCreationProof
    {
        internal DataScopeObjectInitializationSources Owner { get; } = owner;
        internal IDbContextTransaction Transaction { get; } = transaction;
        internal string ResourceKey { get; } = resourceKey;
        internal IReadOnlyDictionary<string, EntityEntry<Dictionary<string, object?>>> Entries { get; } = entries;
    }

    // 主键本地查找只核该新目标；关闭Local getter的自动全量DetectChanges，避免结构创建按目标二次方扫描。
    private void ValidateAddedTarget(DataScopeResource resource, string id, DataScopeObjectInitializationSource.Created source)
    {
        if (!transactions.IsActive || source.Caller is not null) throw Invalid();
        var key = db.Model.FindEntityType(resource.ModelName)?.FindPrimaryKey();
        if (key?.Properties.Count != 1 || key.Properties[0].Name != resource.IdField) throw Invalid();
        var detect = db.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var entry = db.Set<Dictionary<string, object?>>(resource.ModelName).Local.FindEntry(id);
            if (entry?.State != EntityState.Added || entry.Entity.Count != source.Target.Values.Count
                || source.Target.Values.Any(value => !entry.Entity.TryGetValue(value.Key, out var actual) || !Equals(actual, value.Value)))
                throw Invalid();
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = detect; }
    }

    // 来源身份与正式binding双重一致；角色只引用已有合同，不能在此新增authorize或新对象read。
    private void ValidateDeclared(DataScopeResource resource, int parentCount, DataScopeObjectInitializationSource.Declared source)
    {
        var caller = source.Caller ?? throw Invalid();
        var registrations = declared.Where(item => item.ResourceKey == resource.ResourceKey && item.Caller == source.Caller).ToArray();
        if (registrations.Length != 1 || source.Binding is null || caller.Execution != source.Binding.Execution || caller.Provider != source.Binding.Provider)
            throw Invalid();
        var registration = registrations[0];
        ValidateCaller(resource, registration.ModuleId, caller);
        var contract = plan.Modules.Single(module => module.ModuleId == registration.ModuleId).DataExecutions
            .SingleOrDefault(contract => contract.Key == source.Binding.Execution);
        if (contract is null || contract.Boundary != "entry" || contract.Provider != source.Binding.Provider
            || source.Binding.Stage != registration.Stage || source.Binding.Branch is not null)
            throw Invalid();
        var parent = contract.Requirements.SingleOrDefault(role => role.Role == registration.ParentRole && role.Stage == registration.Stage);
        var candidate = contract.Requirements.SingleOrDefault(role => role.Role == registration.CandidateRole && role.Stage == registration.Stage);
        if (parent is null || parent.Shape is not ("object" or "objects") || !parent.Resources.Contains(resource.ResourceKey)
            || parentCount < parent.MinimumCount || parent.MaximumCount is { } maximum && parentCount > maximum
            || candidate is null || candidate.Shape != "candidate" || !candidate.Resources.Contains(resource.ResourceKey))
            throw Invalid();
        execution.Validate([source.Binding], resource.ResourceKey);
    }

    // 新图父由实际创建者给出，完整事实与参与集合一致；不借外部单父角色给新父追加读取或编辑。
    private void ValidateCreatedParents(DataScopeResource resource, IReadOnlyList<string> parents, string newObjectId,
        DataScopeObjectInitializationSource.DeclaredFromCreatedParents source)
    {
        var caller = source.Caller ?? throw Invalid();
        var matches = createdParents.Where(item => item.ResourceKey == resource.ResourceKey && item.Caller == source.Caller).ToArray();
        if (!transactions.IsActive || matches.Length != 1 || source.Binding is null
            || caller.Execution != source.Binding.Execution || caller.Provider != source.Binding.Provider
            || source.Parents is null || source.Parents.Count != parents.Count
            || source.Parents.Select(parent => parent.ObjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != parents.Count)
            throw Invalid();
        var registration = matches[0];
        ValidateCaller(resource, registration.ModuleId, caller);
        var parentIds = parents.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var parent in source.Parents)
            if (parent.ResourceKey != resource.ResourceKey || parent.ObjectId == newObjectId
                || !parentIds.Contains(parent.ObjectId)
                || parent.Values is null || !parent.Values.TryGetValue(resource.IdField, out var key)
                || key is not string id || id != parent.ObjectId) throw Invalid();
        var contract = plan.Modules.Single(module => module.ModuleId == registration.ModuleId).DataExecutions
            .SingleOrDefault(item => item.Key == source.Binding.Execution);
        var candidate = contract?.Requirements.SingleOrDefault(item => item.Role == registration.CandidateRole && item.Stage == registration.Stage);
        if (contract is null || contract.Boundary != "entry" || contract.Provider != source.Binding.Provider
            || source.Binding.Stage != registration.Stage || source.Binding.Branch is not null
            || candidate is null || candidate.Shape != "candidate" || !candidate.Resources.Contains(resource.ResourceKey)) throw Invalid();
        execution.Validate([source.Binding], resource.ResourceKey);
    }

    // 类型取自服务端有限DI登记；即使同名类型或方法存在，也不能替代精确登记和活动owner身份。
    private void ValidateCaller(DataScopeResource resource, string moduleId, DataExecutionInternalBinding caller)
    {
        var module = plan.Modules.SingleOrDefault(module => module.ModuleId == moduleId);
        if (resource.ModuleId != moduleId || module?.BackendBehaviorKey != caller.Provider || caller.Caller is null || string.IsNullOrWhiteSpace(caller.Member)
            || !caller.Caller.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Any(method => method.Name == caller.Member))
            throw Invalid();
    }

    // 统一来源错误不暴露配置内容，且不能成为旧初始化的回退分支。
    private static ActionDataAccessException Invalid() => new(400, "ActionDataAccess.InitializationSourceInvalid");
}
