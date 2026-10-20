namespace ICP.Platform.Kernel.ModulePlanning;

public static partial class ActionDataAccessCompiler
{
    // 输入只能引用真实PageRef读取或原纯query合同；声明本身不产生第二份数据要求。
    private static IReadOnlyList<FeatureDeclaration> BindInputReadSources(IReadOnlyList<FeatureDeclaration> features,
        IReadOnlyDictionary<string, DataExecutionContract> contracts)
    {
        var reads = features.SelectMany(feature => feature.DataAccess).Where(item => item.Surface == "read")
            .GroupBy(item => (item.ModuleId, item.ViewKey)).ToDictionary(group => group.Key, group => group.ToArray());
        return features.Select(feature =>
        {
            var additions = new List<ActionDataAccessDeclaration>();
            foreach (var button in feature.DataAccess.Where(item => item.InputReadSourceKey is not null))
            {
                var key = button.InputReadSourceKey!;
                if (button.BusinessBinding is null || button.ModuleId != feature.ModuleId || string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("INPUT_READ_SOURCE_INVALID");
                var pages = reads.GetValueOrDefault((button.ModuleId, key)) ?? [];
                var querySource = contracts.GetValueOrDefault(key);
                if (querySource is not null && !DataExecutionContracts.IsReadQuery(querySource.Requirements)) querySource = null;
                if (pages.Length == 0 ? querySource is null
                    : querySource is not null && pages[0].Execution != querySource.Key || pages.Any(page => !SameReadSource(page, pages[0])))
                    throw new InvalidOperationException("INPUT_READ_SOURCE_INVALID");
                var source = pages.Length != 0 ? pages[0] : new ActionDataAccessDeclaration(button.ModuleId, key, "read", "", key,
                    querySource!.Requirements[0].Resources, []);
                if (source.BusinessBinding is not null || source.AdditionalActions.Count != 0
                    || !contracts.TryGetValue(source.Execution, out var contract) || contract.ModuleId != button.ModuleId
                    || contract.Boundary != "entry" || contract.Branches.Count != 0 || contract.AllowedAdditionalActions.Count != 0
                    || contract.Requirements.Count == 0 || contract.Requirements.Any(requirement => requirement.Stage == "submit"
                        || requirement.AnyOf.Count != 1 || !requirement.AnyOf[0].SequenceEqual(["read"])
                        || requirement.ResourceAnyOf is not null)
                    || !contract.FeatureBehaviors.Any(behavior => behavior == feature.BehaviorKey || feature.ReadBehaviors.Contains(behavior)))
                    throw new InvalidOperationException("INPUT_READ_SOURCE_INVALID");
                // 多个同一来源的按钮只复用相同内容；已经在该位置的不同内容不可被引用覆盖。
                var existing = feature.DataAccess.Concat(additions).SingleOrDefault(item => item.Id == source.Id);
                if (existing is not null && !SameReadSource(existing, source))
                    throw new InvalidOperationException("INPUT_READ_SOURCE_INVALID");
                if (existing is null) additions.Add(source);
            }
            return additions.Count == 0 ? feature : feature with { DataAccess = [.. feature.DataAccess, .. additions] };
        }).ToArray();
    }

    // 同一来源可出现于多个原功能位置，但身份及要求必须完全相同。
    private static bool SameReadSource(ActionDataAccessDeclaration left, ActionDataAccessDeclaration right) =>
        left.Id == right.Id && left.Execution == right.Execution && left.BusinessBinding == right.BusinessBinding
        && left.Object == right.Object && SameObjects(left.ObjectRequirements, right.ObjectRequirements)
        && left.Resources.SequenceEqual(right.Resources) && left.Actions.SequenceEqual(right.Actions)
        && left.AdditionalActions.SequenceEqual(right.AdditionalActions);
}
