namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>按钮自身声明的数据要求；身份由模块、View 和动作结构派生，不形成第二份操作映射。</summary>
public sealed record ActionDataAccessDeclaration(string ModuleId, string ViewKey, string Surface, string ActionKey,
    string Execution, IReadOnlyList<string> Resources, IReadOnlyList<string> Actions)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string? Object { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? BusinessBinding { get; init; }
    public IReadOnlyList<string> AdditionalActions { get; init; } = [];
    public string Id => $"{Uri.EscapeDataString(ModuleId)}/view:{Uri.EscapeDataString(ViewKey)}/{Surface}"
        + (Surface == "read" ? "" : $"/action:{Uri.EscapeDataString(ActionKey)}");
}

/// <summary>同一正式声明及其合法功能位置，供前后端共同消费。</summary>
public sealed record ActionDataAccessRequirement(string Id, string ModuleId, string ViewKey, string Surface, string ActionKey,
    string Execution, IReadOnlyList<string> Resources, IReadOnlyList<string> Actions, IReadOnlyList<string> FeaturePositions)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string? Object { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? BusinessBinding { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DataExecutionRequirement>? Requirements { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Provider { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderModuleId { get; init; }
}

/// <summary>从既有功能展开中的直接按钮声明编译数据要求，不增加功能授权点。</summary>
public static class ActionDataAccessCompiler
{
    /// <summary>拒绝动作、身份、归属及资源引用错误，并合并同一 View 的合法功能出现位置。</summary>
    public static IReadOnlyList<ActionDataAccessRequirement> Compile(IReadOnlyList<FeatureDeclaration> features,
        IReadOnlyList<DataActionDeclaration> actions, IReadOnlyList<DataExecutionContract>? executions = null,
        IReadOnlyList<DataExecutionProviderContract>? providers = null)
    {
        var contracts = DataExecutionContracts.Compile(DataExecutionProviders.Resolve(executions ?? [], providers ?? []), actions);
        var positions = FeatureCatalogCompiler.Compile(features).ToDictionary(point => (point.ModuleId, point.ReferenceKey));
        var resources = features.SelectMany(feature => feature.DataResources).ToHashSet(StringComparer.Ordinal);
        var entries = features.SelectMany(feature => feature.DataAccess.Select(declaration => (feature, declaration))).ToArray();
        if (contracts.Values.Any(contract => contract.Boundary == "entry" && !entries.Any(entry => entry.declaration.Execution == contract.Key)))
            throw new InvalidOperationException("DATA_EXECUTION_BINDING_MISSING");
        foreach (var (feature, declaration) in entries)
        {
            if (declaration.BusinessBinding is not null)
            {
                if (declaration.ModuleId != feature.ModuleId || string.IsNullOrWhiteSpace(declaration.ViewKey)
                    || string.IsNullOrWhiteSpace(declaration.ActionKey) || declaration.Execution.Length != 0
                    || declaration.Resources.Count > 1 || declaration.Object is not null && declaration.Resources.Count != 1
                    || declaration.Resources.Any(resource => !resources.Contains(resource))
                    || declaration.AdditionalActions.Count != 0
                    || declaration.Object is null && declaration.Actions.Count != 0
                    || declaration.Object is not null && (string.IsNullOrWhiteSpace(declaration.Object) || declaration.Actions.Count == 0)
                    || declaration.Actions.Distinct(StringComparer.Ordinal).Count() != declaration.Actions.Count
                    || declaration.Actions.Any(key => !actions.Any(action => action.Key == key))
                    || declaration.BusinessBinding != feature.BehaviorKey)
                    throw new InvalidOperationException("BUTTON_DATA_ACCESS_DECLARATION_INVALID");
                continue;
            }
            if (declaration.ModuleId != feature.ModuleId || string.IsNullOrWhiteSpace(declaration.ViewKey)
                || string.IsNullOrWhiteSpace(declaration.Execution) || declaration.Surface is not ("read" or "export" or "lookupBindings" or "featureActions" or "pageActions" or "rowActions" or "outputActions" or "detailContributions")
                || declaration.Surface != "read" && string.IsNullOrWhiteSpace(declaration.ActionKey)
                || declaration.Actions.Distinct(StringComparer.Ordinal).Count() != declaration.Actions.Count
                || declaration.Actions.Any(key => !actions.Any(action => action.Key == key))
                || declaration.Resources.Count == 0 || declaration.Resources.Distinct(StringComparer.Ordinal).Count() != declaration.Resources.Count
                || declaration.Resources.Any(resource => !resources.Contains(resource)))
                throw new InvalidOperationException("ACTION_DATA_ACCESS_DECLARATION_INVALID");
            if (!contracts.TryGetValue(declaration.Execution, out var contract))
                throw new InvalidOperationException("DATA_EXECUTION_UNKNOWN");
            if (contract.ModuleId != declaration.ModuleId || contract.Boundary != "entry"
                || contract.TargetResources is { } targets && !targets.Order(StringComparer.Ordinal).SequenceEqual(declaration.Resources.Order(StringComparer.Ordinal))
                || declaration.Resources.Any(resource => !contract.Requirements.Any(requirement => requirement.Resources.Contains(resource)))
                || contract.Requirements.Any(requirement => !requirement.Resources.Any(resource => declaration.Resources.Contains(resource))))
                throw new InvalidOperationException("DATA_EXECUTION_BINDING_INVALID");
            if (declaration.Actions.Count != 0
                || declaration.AdditionalActions.Any(action => !contract.AllowedAdditionalActions.Contains(action))
                || !contract.FeatureBehaviors.Any(behavior => behavior == feature.BehaviorKey || feature.ReadBehaviors.Contains(behavior)))
                throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
        }
        if (entries.GroupBy(entry => (entry.feature.ModuleId, Reference: entry.feature.ReferenceKey ?? entry.feature.Key, entry.declaration.Id))
            .Any(group => group.Count() != 1)) throw new InvalidOperationException("ACTION_DATA_ACCESS_IDENTITY_CONFLICT");
        return entries.GroupBy(entry => entry.declaration.Id, StringComparer.Ordinal).Select(group =>
        {
            var first = group.First().declaration;
            if (first.BusinessBinding is not null)
            {
                if (group.Any(entry => entry.declaration.BusinessBinding != first.BusinessBinding || entry.declaration.Object != first.Object
                    || !entry.declaration.Actions.SequenceEqual(first.Actions) || !entry.declaration.Resources.SequenceEqual(first.Resources)))
                    throw new InvalidOperationException("ACTION_DATA_ACCESS_IDENTITY_CONFLICT");
                return new ActionDataAccessRequirement(first.Id, first.ModuleId, first.ViewKey, first.Surface, first.ActionKey,
                    "", first.Resources, first.Actions, group.Select(entry => positions[(entry.feature.ModuleId,
                        entry.feature.ReferenceKey ?? entry.feature.Key)].Position).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
                { Object = first.Object, BusinessBinding = first.BusinessBinding };
            }
            var contract = contracts[first.Execution];
            if (group.Any(entry => entry.declaration.Execution != first.Execution
                || !entry.declaration.Actions.SequenceEqual(first.Actions) || !entry.declaration.Resources.SequenceEqual(first.Resources)
                || !entry.declaration.AdditionalActions.SequenceEqual(first.AdditionalActions)))
                throw new InvalidOperationException("ACTION_DATA_ACCESS_IDENTITY_CONFLICT");
            return new ActionDataAccessRequirement(first.Id, first.ModuleId, first.ViewKey, first.Surface, first.ActionKey,
                first.Execution, first.Resources, contract.Requirements.SelectMany(requirement =>
                    requirement.AnyOf.Concat(requirement.ResourceAnyOf?.Values.SelectMany(branches => branches) ?? []))
                    .SelectMany(branch => branch).Concat(first.AdditionalActions).Distinct(StringComparer.Ordinal).ToArray(),
                group.Select(entry => positions[(entry.feature.ModuleId,
                    entry.feature.ReferenceKey ?? entry.feature.Key)].Position).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
            {
                Provider = contract.Provider,
                ProviderModuleId = contract.ProviderModuleId,
                Requirements = contract.Requirements.Select(requirement => requirement with
                { Resources = requirement.Resources.Where(first.Resources.Contains).ToArray(),
                    ResourceAnyOf = requirement.ResourceAnyOf?.Where(item => first.Resources.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                    AnyOf = requirement.AnyOf.Select(branch => (IReadOnlyList<string>)branch.Concat(first.AdditionalActions).Distinct(StringComparer.Ordinal).ToArray()).ToArray() }).ToArray()
            };
        }).OrderBy(requirement => requirement.Id, StringComparer.Ordinal).ToArray();
    }
}
