using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using ICP.Platform.Presentation;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>规范资源映射来自标准描述符及模块的显式资源贡献，动作来自正式候选。</summary>
public sealed class DataScopeCatalog(IStandardCrudResourceRegistry resources, CanonicalModulePlan plan,
    EfPlatformDbContext db, DataScopeIdentities identities, ITemporalInputNormalizer? temporalInputNormalizer = null,
    IEnumerable<IDataScopeBusinessConditionProvider>? conditionProviders = null,
    IEnumerable<IDataScopeEnumFieldProvider>? enumProviders = null,
    IEnumerable<DataScopeResource>? resourceContributions = null,
    IDataScopeResourceCatalog? preparedResources = null)
{
    private DataScopeConditions Conditions => new(this, db, identities, temporalInputNormalizer, conditionProviders ?? [], enumProviders ?? []);
    public IReadOnlyList<DataActionDeclaration> Actions => plan.DataActionCatalog;
    internal DataScopeDirectSubjects DirectSubjects => new(this, db, identities);

    /// <summary>对象操作只消费owner选用的全局动作；旧资源未声明时保持既有目录。</summary>
    public IReadOnlyList<DataActionDeclaration> ActionsFor(string resourceKey)
    {
        Resolve(resourceKey);
        var keys = preparedResources?.ActionKeysFor(resourceKey);
        return keys is null ? Actions : Actions.Where(action => keys.Contains(action.Key, StringComparer.Ordinal)).ToArray();
    }
    public IReadOnlyList<DataScopeBusinessConditionDefinition> BusinessConditions
    {
        get { Conditions.ValidateRegistration(); return Conditions.BusinessTypes; }
    }
    public IReadOnlyList<string>? EnumValues(string resourceKey, string field) => Conditions.EnumValues(resourceKey, field);
    public IReadOnlyList<DataScopeResource> Resources => preparedResources?.Resources ?? ReadResources();

    // 专用命令资源不必开放普通 CRUD；目录仍验证真实活动 owner、模型和唯一主键，不推断字段身份。
    private IReadOnlyList<DataScopeResource> ReadResources()
    {
        var declared = (resourceContributions ?? []).ToArray();
        foreach (var resource in declared)
        {
            if (string.IsNullOrWhiteSpace(resource.ResourceKey) || string.IsNullOrWhiteSpace(resource.ModuleId)
                || string.IsNullOrWhiteSpace(resource.ModelName) || string.IsNullOrWhiteSpace(resource.IdField)
                || !plan.Modules.Any(module => module.ModuleId == resource.ModuleId))
                throw new InvalidOperationException("DATA_SCOPE_RESOURCE_OWNER_INVALID");
            ValidateModelOwner(resource);
            var model = db.Model.FindEntityType(resource.ModelKey)
                ?? throw new InvalidOperationException("DATA_SCOPE_MODEL_UNAVAILABLE");
            if (model.FindPrimaryKey()?.Properties is not { Count: 1 } keys || keys[0].Name != resource.IdField
                || keys[0].ClrType != typeof(string))
                throw new InvalidOperationException("DATA_SCOPE_RESOURCE_ID_INVALID");
        }
        var all = resources.GetAll().Select(resource => new DataScopeResource(resource.ResourceKey, resource.ModuleId, resource.ModelName, resource.IdField))
            .Concat(declared).ToArray();
        if (all.Select(resource => resource.ResourceKey).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new InvalidOperationException("DATA_SCOPE_RESOURCE_DUPLICATE");
        return all;
    }

    /// <summary>定位明确登记的资源；不从模型名或页面猜测身份。</summary>
    public DataScopeResource Resolve(string key) => Resources.SingleOrDefault(resource => resource.ResourceKey == key)
        ?? throw new RolePermissionValidationException("数据资源未登记。", "DATA_SCOPE_RESOURCE_UNKNOWN", field: "resourceKey");

    // 规范授权目标不要求公开普通CRUD；事实仍来自已验证owner/model/IdField的实际对象行。
    internal async Task<IReadOnlyDictionary<string, object?>?> ReadObjectAsync(string resourceKey, string objectId, CancellationToken token)
    {
        var resource = Resolve(resourceKey);
        return await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(resource.ModelKey)).AsNoTracking()
            .SingleOrDefaultAsync(row => EF.Property<string>(row, resource.IdField) == objectId, token);
    }

    /// <summary>返回正式模型的原始规范字段，不包含 Display companion。</summary>
    public IReadOnlyList<IProperty> Fields(string key)
    {
        var resource = Resolve(key);
        ValidateModelOwner(resource);
        var model = db.Model.FindEntityType(db.ResolveModelKey(resource.ModelKey))
            ?? throw new InvalidOperationException("DATA_SCOPE_MODEL_UNAVAILABLE");
        return model.GetProperties().Where(property => !property.Name.StartsWith("__", StringComparison.Ordinal)).ToArray();
    }

    // 名称只用于寻址；归属从正式编译目录核对，不能从字符串前缀推断。
    private void ValidateModelOwner(DataScopeResource resource)
    {
        var model = db.PersistencePlan.Models.SingleOrDefault(model => model.LogicalKey == resource.ModelKey)
            ?? throw new InvalidOperationException("DATA_SCOPE_MODEL_UNAVAILABLE");
        if (model.OwnerModuleId != resource.ModuleId)
            throw new InvalidOperationException("DATA_SCOPE_MODEL_OWNER_MISMATCH");
    }

    /// <summary>验证完整根树的身份、层级和已注册主体、条件。</summary>
    public void Validate(DataScopeRuleDto rule)
    {
        Resolve(rule.ResourceKey);
        Conditions.ValidateRegistration();
        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateNode(rule, null);

        // 输入树的嵌套位置决定真实父身份，不能相信节点自报的 ParentId。
        void ValidateNode(DataScopeRuleDto node, string? parentId)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 128 || !nodeIds.Add(node.Id)
                || node.RootId != rule.Id || node.ParentId != parentId || node.ResourceKey != rule.ResourceKey
                || string.IsNullOrWhiteSpace(node.Name) || node.Name.Length > 256
                || node.Children is null || node.Subjects is null)
                throw new RolePermissionValidationException("规则树身份、父子关系或资源无效。", "DATA_SCOPE_RULE_TREE_INVALID", field: "rule");
            try { Conditions.Validate(node.ResourceKey, node.Condition); }
            catch (RolePermissionValidationException exception) when (exception.Args.ContainsKey("conditionPath"))
            { throw new RolePermissionValidationException(parentId is null ? exception.Message : $"节点 {node.Id}：{exception.Message}",
                exception.Code, new Dictionary<string, object?>(exception.Args) { ["nodeId"] = node.Id }, exception.Field); }
            foreach (var subject in node.Subjects)
            {
                if (string.IsNullOrWhiteSpace(subject.Id) || subject.Id.Length > 128 || !subjectIds.Add(subject.Id)
                    || subject.Actions is null || subject.Actions.Any(action =>
                        !Actions.Any(defined => defined.Key == action.Key) || action.Value is not ("allow" or "deny" or "unset")))
                    throw new RolePermissionValidationException("主体授权项身份或读取三态无效。", "DATA_SCOPE_SUBJECT_ENTRY_INVALID", field: "subjects");
                identities.Validate(subject.Type, subject.SubjectId, subject.MatchOptions);
            }
            foreach (var child in node.Children) ValidateNode(child, node.Id);
        }
    }

    /// <summary>保存时规范化默认匹配选项和全部日期叶子；固定 Instant 不随后续查看者重新解释。</summary>
    public DataScopeRuleDto NormalizeForSave(DataScopeRuleDto rule)
    {
        return NormalizeNode(rule);

        // 日期和主体选项逐节点规范化，保存审计只记录最终配置。
        DataScopeRuleDto NormalizeNode(DataScopeRuleDto node)
        {
            try
            {
                return node with
                {
                    Subjects = node.Subjects.Select(subject => subject.MatchOptions?.IncludeDescendants == false
                        ? subject with { MatchOptions = null } : subject).ToArray(),
                    Condition = Conditions.Normalize(node.ResourceKey, node.Condition),
                    Children = node.Children.Select(NormalizeNode).ToArray()
                };
            }
            catch (RolePermissionValidationException exception) when (!exception.Args.ContainsKey("nodeId"))
            {
                throw new RolePermissionValidationException(exception.Message, exception.Code,
                    new Dictionary<string, object?>(exception.Args) { ["nodeId"] = node.Id }, exception.Field);
            }
        }
    }

    /// <summary>读取条件只投影有读取贡献的叶子。</summary>
    public System.Linq.Expressions.Expression? BuildReadCondition(string resourceKey, DataScopeConditionDto condition,
        System.Linq.Expressions.ParameterExpression row, DataScopeResolvedIdentities facts) =>
        Conditions.BuildRead(resourceKey, condition, row, facts);

    /// <summary>无读取贡献的整节点不应解析主体或产生读取意见。</summary>
    public bool HasReadContribution(string resourceKey, DataScopeConditionDto condition) => Conditions.HasReadContribution(resourceKey, condition);

    /// <summary>完整判断为后续普通写入提供同一扩展语义；文本属性交数据库比较。</summary>
    public Task<bool> MatchesAsync(string resourceKey, string objectId, DataScopeConditionDto condition,
        DataScopeResolvedIdentities facts, CancellationToken token)
    {
        Conditions.Validate(resourceKey, condition);
        return Conditions.MatchesAsync(resourceKey, objectId, condition, facts, token);
    }

    /// <summary>创建前用完整候选判定条件，属性比较仍交正式数据库排序规则。</summary>
    public Task<bool> MatchesCandidateAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate,
        DataScopeConditionDto condition, DataScopeResolvedIdentities facts, CancellationToken token) =>
        Conditions.MatchesCandidateAsync(resourceKey, candidate, condition, facts, token);

    /// <summary>枚举组合树实际引用的可信当前身份值。</summary>
    public IEnumerable<string> ContextKeys(DataScopeConditionDto condition) => Conditions.ContextKeys(condition);
}
