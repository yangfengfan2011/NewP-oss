namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>按钮自身声明的数据要求；身份由模块、View 和动作结构派生，不形成第二份操作映射。</summary>
public sealed record ActionDataAccessDeclaration(string ModuleId, string ViewKey, string Surface, string ActionKey,
    string Execution, IReadOnlyList<string> Resources, IReadOnlyList<string> Actions)
{
    public string Id => $"{Uri.EscapeDataString(ModuleId)}/view:{Uri.EscapeDataString(ViewKey)}/{Surface}"
        + (Surface == "read" ? "" : $"/action:{Uri.EscapeDataString(ActionKey)}");
}

/// <summary>同一正式声明及其合法功能位置，供前后端共同消费。</summary>
public sealed record ActionDataAccessRequirement(string Id, string ModuleId, string ViewKey, string Surface, string ActionKey,
    string Execution, IReadOnlyList<string> Resources, IReadOnlyList<string> Actions, IReadOnlyList<string> FeaturePositions);

/// <summary>从既有功能展开中的直接按钮声明编译数据要求，不增加功能授权点。</summary>
public static class ActionDataAccessCompiler
{
    /// <summary>拒绝动作、身份、归属及资源引用错误，并合并同一 View 的合法功能出现位置。</summary>
    public static IReadOnlyList<ActionDataAccessRequirement> Compile(IReadOnlyList<FeatureDeclaration> features,
        IReadOnlyList<DataActionDeclaration> actions)
    {
        var positions = FeatureCatalogCompiler.Compile(features).ToDictionary(point => (point.ModuleId, point.ReferenceKey));
        var resources = features.SelectMany(feature => feature.DataResources).ToHashSet(StringComparer.Ordinal);
        var entries = features.SelectMany(feature => feature.DataAccess.Select(declaration => (feature, declaration))).ToArray();
        foreach (var (feature, declaration) in entries)
        {
            if (declaration.ModuleId != feature.ModuleId || string.IsNullOrWhiteSpace(declaration.ViewKey)
                || string.IsNullOrWhiteSpace(declaration.Execution) || declaration.Surface is not ("read" or "pageActions" or "rowActions" or "outputActions")
                || declaration.Surface != "read" && string.IsNullOrWhiteSpace(declaration.ActionKey)
                || declaration.Actions.Count == 0 || declaration.Actions.Distinct(StringComparer.Ordinal).Count() != declaration.Actions.Count
                || declaration.Actions.Any(key => !actions.Any(action => action.Key == key))
                || declaration.Resources.Count == 0 || declaration.Resources.Distinct(StringComparer.Ordinal).Count() != declaration.Resources.Count
                || declaration.Resources.Any(resource => !resources.Contains(resource)))
                throw new InvalidOperationException("ACTION_DATA_ACCESS_DECLARATION_INVALID");
        }
        if (entries.GroupBy(entry => (entry.feature.ModuleId, Reference: entry.feature.ReferenceKey ?? entry.feature.Key, entry.declaration.Id))
            .Any(group => group.Count() != 1)) throw new InvalidOperationException("ACTION_DATA_ACCESS_IDENTITY_CONFLICT");
        return entries.GroupBy(entry => entry.declaration.Id, StringComparer.Ordinal).Select(group =>
        {
            var first = group.First().declaration;
            if (group.Any(entry => entry.declaration.Execution != first.Execution
                || !entry.declaration.Actions.SequenceEqual(first.Actions) || !entry.declaration.Resources.SequenceEqual(first.Resources)))
                throw new InvalidOperationException("ACTION_DATA_ACCESS_IDENTITY_CONFLICT");
            return new ActionDataAccessRequirement(first.Id, first.ModuleId, first.ViewKey, first.Surface, first.ActionKey,
                first.Execution, first.Resources, first.Actions, group.Select(entry => positions[(entry.feature.ModuleId,
                    entry.feature.ReferenceKey ?? entry.feature.Key)].Position).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }).OrderBy(requirement => requirement.Id, StringComparer.Ordinal).ToArray();
    }
}
