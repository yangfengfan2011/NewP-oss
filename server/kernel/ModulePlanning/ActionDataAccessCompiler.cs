namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>按钮声明中的一个对象及其动作；资源身份由正式对象绑定派生，不由作者另配。</summary>
public sealed record ButtonObjectRequirement([property: System.Text.Json.Serialization.JsonPropertyName("object")] string Object,
    IReadOnlyList<string> Actions, string ResourceKey);

/// <summary>按钮自身声明的数据要求；身份由模块、View 和动作结构派生，不形成第二份操作映射。</summary>
public sealed record ActionDataAccessDeclaration(string ModuleId, string ViewKey, string Surface, string ActionKey,
    string Execution, IReadOnlyList<string> Resources, IReadOnlyList<string> Actions)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string? Object { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? BusinessBinding { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ButtonObjectRequirement>? ObjectRequirements { get; init; }
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
    public IReadOnlyList<ButtonObjectRequirement>? ObjectRequirements { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DataExecutionRequirement>? Requirements { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Provider { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderModuleId { get; init; }
    /// <summary>把兼容单对象与正式多对象投影为同一只读要求集合；不添加隐式动作。</summary>
    public IReadOnlyList<ButtonObjectRequirement> GetObjectRequirements() => ObjectRequirements
        ?? (Object is null ? [] : [new(Object, Actions, Resources.Single())]);
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
        if (contracts.Values.Any(contract => contract.Boundary == "entry" && !entries.Any(entry => entry.declaration.Execution == contract.Key
            || QuerySource(entry.declaration, contracts)?.Key == contract.Key)))
            throw new InvalidOperationException("DATA_EXECUTION_BINDING_MISSING");
        foreach (var (feature, declaration) in entries)
        {
            if (declaration.BusinessBinding is not null)
            {
                var objects = declaration.ObjectRequirements;
                if (declaration.ModuleId != feature.ModuleId || string.IsNullOrWhiteSpace(declaration.ViewKey)
                    || string.IsNullOrWhiteSpace(declaration.ActionKey) || declaration.Execution.Length != 0
                    || declaration.Resources.Distinct(StringComparer.Ordinal).Count() != declaration.Resources.Count
                    || declaration.Resources.Any(resource => !resources.Contains(resource))
                    || declaration.AdditionalActions.Count != 0 || declaration.BusinessBinding != feature.BehaviorKey
                    || (objects is null
                        ? declaration.Resources.Count > 1 || declaration.Object is not null && declaration.Resources.Count != 1
                            || declaration.Object is null && declaration.Actions.Count != 0
                            || declaration.Object is not null && (string.IsNullOrWhiteSpace(declaration.Object) || !ValidActions(declaration.Actions, actions))
                        : declaration.Object is not null || declaration.Actions.Count != 0 || objects.Count == 0
                            || objects.Any(item => item is null || string.IsNullOrWhiteSpace(item.Object) || !ValidActions(item.Actions, actions))
                            || objects.Select(item => item.Object).Distinct(StringComparer.Ordinal).Count() != objects.Count
                            || objects.Select(item => item.ResourceKey).Distinct(StringComparer.Ordinal).Count() != objects.Count
                            || !objects.Select(item => item.ResourceKey).Order(StringComparer.Ordinal).SequenceEqual(declaration.Resources.Order(StringComparer.Ordinal))))
                    throw new InvalidOperationException("BUTTON_DATA_ACCESS_DECLARATION_INVALID");
                if (declaration.Surface == "export" && QuerySource(declaration, contracts) is null)
                    throw new InvalidOperationException("BUTTON_QUERY_SOURCE_INVALID");
                continue;
            }
            if (declaration.ModuleId != feature.ModuleId || string.IsNullOrWhiteSpace(declaration.ViewKey)
                || declaration.ObjectRequirements is not null || string.IsNullOrWhiteSpace(declaration.Execution) || declaration.Surface is not ("read" or "export" or "lookupBindings" or "featureActions" or "pageActions" or "rowActions" or "outputActions" or "detailContributions")
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
                    || !entry.declaration.Actions.SequenceEqual(first.Actions) || !entry.declaration.Resources.SequenceEqual(first.Resources)
                    || !SameObjects(entry.declaration.ObjectRequirements, first.ObjectRequirements)))
                    throw new InvalidOperationException("ACTION_DATA_ACCESS_IDENTITY_CONFLICT");
                return new ActionDataAccessRequirement(first.Id, first.ModuleId, first.ViewKey, first.Surface, first.ActionKey,
                    "", first.Resources, first.Actions, group.Select(entry => positions[(entry.feature.ModuleId,
                        entry.feature.ReferenceKey ?? entry.feature.Key)].Position).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
                { Object = first.Object, BusinessBinding = first.BusinessBinding, ObjectRequirements = first.ObjectRequirements,
                    Requirements = QuerySource(first, contracts)?.Requirements, Provider = QuerySource(first, contracts)?.Provider,
                    ProviderModuleId = QuerySource(first, contracts)?.ProviderModuleId };
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

    // 每条对象要求独立持有非空、唯一且已登记的动作，不从其他条目补要求。
    private static bool ValidActions(IReadOnlyList<string> keys, IReadOnlyList<DataActionDeclaration> actions) => keys is not null
        && keys.Count > 0 && keys.Distinct(StringComparer.Ordinal).Count() == keys.Count
        && keys.All(key => actions.Any(action => action.Key == key));

    // 标准输出按钮只适配其原查询来源；read是查询能力约束，不通过伪造EID或逐项判权实现。
    private static DataExecutionContract? QuerySource(ActionDataAccessDeclaration declaration, IReadOnlyDictionary<string, DataExecutionContract> contracts)
    {
        if (declaration.Surface != "export" || declaration.BusinessBinding is null
            || !contracts.TryGetValue(declaration.BusinessBinding, out var contract) || contract.ModuleId != declaration.ModuleId
            || contract.Boundary != "entry" || contract.Provider != "standardCrud.export" || contract.Requirements.Count != 1
            || !contract.FeatureBehaviors.Contains(declaration.BusinessBinding, StringComparer.Ordinal)) return null;
        var objects = declaration.ObjectRequirements ?? (declaration.Object is null ? [] : new[] { new ButtonObjectRequirement(declaration.Object, declaration.Actions, declaration.Resources.Single()) });
        var query = contract.Requirements[0];
        var required = objects.SingleOrDefault(item => query.Resources.SequenceEqual([item.ResourceKey]));
        return required is not null && required.Actions.SequenceEqual(["read"]) && query.Role == "query" && query.Stage == "read"
            && query.Shape == "query" && query.AnyOf.Count == 1
            && query.AnyOf[0].SequenceEqual(["read"]) && query.MinimumCount == 0 && query.MaximumCount is null
            && query.Branch is null && query.ReadResolverKey is null && query.ResourceAnyOf is null ? contract : null;
    }

    // 同一authoring在多个出现位置独立解析；比较内容而非数组实例身份。
    private static bool SameObjects(IReadOnlyList<ButtonObjectRequirement>? left, IReadOnlyList<ButtonObjectRequirement>? right) =>
        left is null ? right is null : right is not null && left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Object == pair.Second.Object && pair.First.ResourceKey == pair.Second.ResourceKey
            && pair.First.Actions.SequenceEqual(pair.Second.Actions));

}
