using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataExecutionAccess
{
    // 只有当前正式按钮明确引用的纯读取源可供其静态功能端点准备输入；原功能和对象读取仍独立生效。
    private bool IsDeclaredInputReadSource(ActionDataAccessRequirement source, DataExecutionContract contract,
        string endpointBehavior, string position)
    {
        if (source.Surface != "read" || source.BusinessBinding is not null || !source.Actions.SequenceEqual(["read"])
            || source.Requirements is not { Count: > 0 } || contract.Branches.Count != 0
            || source.Requirements.Any(item => item.Stage == "submit" || item.AnyOf.Count != 1 || !item.AnyOf[0].SequenceEqual(["read"])))
            return false;
        var point = plan.FeatureCatalog.SingleOrDefault(item => item.Position == position);
        if (point is null || point.ModuleId != source.ModuleId || point.BehaviorKey != endpointBehavior
            || !contract.FeatureBehaviors.Any(behavior => point.ReadBehaviors.Contains(behavior, StringComparer.Ordinal)))
            return false;
        var feature = plan.Modules.Single(item => item.ModuleId == point.ModuleId).Features
            .SingleOrDefault(item => (item.ReferenceKey ?? item.Key) == point.ReferenceKey);
        return feature?.DataAccess.Count(button => button.ModuleId == source.ModuleId && button.BusinessBinding == endpointBehavior
            && button.InputReadSourceKey == source.ViewKey) == 1;
    }
}
