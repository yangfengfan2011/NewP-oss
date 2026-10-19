using System.Reflection;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>初始化来源只做零SQL事实核对，父读取/编辑与候选创建仍由同一正式执行负责。</summary>
public sealed class DataScopeObjectInitializationSources(DataScopeCatalog catalog, CanonicalModulePlan plan, IDataExecutionAccess execution,
    IEnumerable<DataScopeObjectInitializationBinding> declared, IEnumerable<DataScopeObjectInitializationLegacyBinding> legacy)
{
    /// <summary>在任何配置锁、读取或克隆前核验真实父集合、新对象和有限调用者来源。</summary>
    public void Validate(string resourceKey, IReadOnlyList<string> parents, string newObjectId, DataScopeObjectInitializationSource source)
    {
        var resource = catalog.Resolve(resourceKey);
        if (source is null || source.Caller is null || source.Target is null || source.Target.Values is null
            || source.Target.ResourceKey != resourceKey || source.Target.ObjectId != newObjectId
            || !source.Target.Values.TryGetValue(resource.IdField, out var actualId) || actualId is not string id || id != newObjectId
            || source.ParentObjectIds is null || source.ParentObjectIds.Any(string.IsNullOrWhiteSpace)
            || !parents.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(source.ParentObjectIds))
            throw Invalid();
        switch (source)
        {
            case DataScopeObjectInitializationSource.Declared current:
                ValidateDeclared(resource, parents.Count, current);
                break;
            case DataScopeObjectInitializationSource.Legacy old:
                var registrations = legacy.Where(item => item.ResourceKey == resourceKey && item.Caller == old.Caller).ToArray();
                if (registrations.Length != 1) throw Invalid();
                ValidateCaller(resource, registrations[0].ModuleId, old.Caller);
                break;
            default: throw Invalid();
        }
    }

    // 来源身份与正式binding双重一致；角色只引用已有合同，不能在此新增authorize或新对象read。
    private void ValidateDeclared(DataScopeResource resource, int parentCount, DataScopeObjectInitializationSource.Declared source)
    {
        var registrations = declared.Where(item => item.ResourceKey == resource.ResourceKey && item.Caller == source.Caller).ToArray();
        if (registrations.Length != 1 || source.Binding is null || source.Caller.Execution != source.Binding.Execution || source.Caller.Provider != source.Binding.Provider)
            throw Invalid();
        var registration = registrations[0];
        ValidateCaller(resource, registration.ModuleId, source.Caller);
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
