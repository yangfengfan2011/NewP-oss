using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>按钮查询只消费同一资源原有的SQL读取来源，不构造对象ID或继承允许结果。</summary>
internal static class ButtonQuerySources
{
    // 每次消费重新核对正式按钮、查询来源和资源；这里只允许原标准导出的纯read查询形状。
    internal static void Validate(CanonicalModulePlan plan, ActionDataAccessRequirement declaration,
        ButtonQueryTarget target, IDataScopeResourceCatalog resources)
    {
        var objects = declaration.GetObjectRequirements();
        var resource = resources.Resources.SingleOrDefault(item => item.ResourceKey == target.ResourceKey);
        var binding = target.Source.Binding;
        var contract = plan.Modules.SelectMany(module => module.DataExecutions).SingleOrDefault(item => item.Key == binding.Execution);
        if (declaration.Surface != "export" || objects.Count != 1 || objects[0].Object != target.ObjectName
            || objects[0].ResourceKey != target.ResourceKey || !objects[0].Actions.SequenceEqual(["read"])
            || resource?.ModelName != target.ObjectName || resource.ModuleId != declaration.ModuleId
            || declaration.BusinessBinding != binding.Execution || binding.Provider != "standardCrud.export" || binding.Stage != "read" || binding.Branch is not null
            || target.Source.Role != "query" || target.Source.TargetSets.Count != 0 || contract is null
            || contract.ModuleId != declaration.ModuleId || contract.Provider != binding.Provider || contract.Boundary != "entry"
            || !contract.FeatureBehaviors.Contains(declaration.BusinessBinding, StringComparer.Ordinal) || contract.Requirements.Count != 1)
            throw new ActionDataAccessException(400, "ButtonExecution.QuerySourceInvalid");
        var query = contract.Requirements[0];
        if (query.Role != "query" || query.Stage != "read" || query.Shape != "query" || !query.Resources.SequenceEqual([target.ResourceKey])
            || query.AnyOf.Count != 1 || !query.AnyOf[0].SequenceEqual(["read"]) || query.MinimumCount != 0 || query.MaximumCount is not null
            || query.Branch is not null || query.ResourceAnyOf is not null || query.ReadResolverKey is not null)
            throw new ActionDataAccessException(400, "ButtonExecution.QuerySourceInvalid");
    }
}
