using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>一个有序角色要求；各要求取交集，AnyOf 中的分支可替代，分支内部动作取交集。</summary>
public sealed record DataExecutionRequirement(string Role, string Stage, string Shape,
    IReadOnlyList<string> Resources, IReadOnlyList<IReadOnlyList<string>> AnyOf,
    int MinimumCount = 1, int? MaximumCount = 1)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Branch { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ReadResolverKey { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? ResourceAnyOf { get; init; }
}

/// <summary>无界面执行的正式固定绑定；API不能伪装为内部调用或成为界面缺声明的回退。</summary>
public sealed record DataExecutionFixedBinding(string Kind, string Identity, string? HttpMethod = null, string? Route = null, string? Caller = null);

/// <summary>执行 owner 的固定合同；入口只能引用，不能替换必要要求。</summary>
public sealed record DataExecutionContract(string Key, string ModuleId, string Provider, string Boundary,
    IReadOnlyList<string> FeatureBehaviors, IReadOnlyList<DataExecutionRequirement> Requirements)
{
    public bool LegacyActions { get; init; }
    public IReadOnlyList<string> AllowedAdditionalActions { get; init; } = [];
    public IReadOnlyList<string> Branches { get; init; } = [];
    public IReadOnlyList<DataExecutionFixedBinding> FixedBindings { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetResource { get; init; }
    /// <summary>执行owner声明的有限异构资源集；与单资源消费者互斥，要求仍由能力owner唯一提供。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? TargetResources { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderModuleId { get; init; }
}

/// <summary>从同一可信 Capsule 读取执行合同，装配时核对 owner、形状和动作目录。</summary>
public static class DataExecutionContracts
{
    /// <summary>读取 owner 提供的执行合同；缺少字段或未知字段拒绝，不能静默忽略下限改写。</summary>
    public static IReadOnlyList<DataExecutionContract> Read(JsonElement capsule, string moduleId)
    {
        if (!capsule.TryGetProperty("dataExecutions", out var executions)) return [];
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        return executions.EnumerateArray().Select(item =>
        {
            // 资源集合只有authoring的短引用入口，不接受内部投影字段作为第二种声明写法。
            if (item.TryGetProperty("targetResources", out _))
                throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
            if (item.GetProperty("provider").GetString() is "entityOwnership.transfer" or "entityOwnership.initialize")
            {
                var ownershipProvider = item.GetProperty("provider").GetString()!;
                if (item.EnumerateObject().Any(property => property.Name is not ("key" or "moduleId" or "provider" or "boundary" or "featureBehaviors" or "resources"))
                    || item.GetProperty("moduleId").GetString() != moduleId)
                    throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
                return new DataExecutionContract(item.GetProperty("key").GetString()!, moduleId, ownershipProvider,
                    item.GetProperty("boundary").GetString()!, item.GetProperty("featureBehaviors").Deserialize<string[]>()!,
                    OwnershipRequirements(ownershipProvider, item.GetProperty("resources").Deserialize<string[]>()!));
            }
            if (!item.GetProperty("provider").GetString()!.StartsWith("standardCrud.", StringComparison.Ordinal)
                && (item.TryGetProperty("resourceKey", out _) || item.TryGetProperty("resources", out _)))
            {
                var scalar = item.TryGetProperty("resourceKey", out var resource);
                var collection = item.TryGetProperty("resources", out var resources);
                var targets = collection ? resources.Deserialize<string[]>(options) : null;
                if (scalar == collection || targets is not null && (targets.Length == 0 || targets.Any(string.IsNullOrWhiteSpace)
                        || targets.Distinct(StringComparer.Ordinal).Count() != targets.Length)
                    || item.EnumerateObject().Any(property => property.Name is not ("key" or "moduleId" or "provider" or "boundary" or "featureBehaviors" or "resourceKey" or "resources" or "fixedBindings"))
                    || item.GetProperty("moduleId").GetString() != moduleId)
                    throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
                return new DataExecutionContract(item.GetProperty("key").GetString()!, moduleId, item.GetProperty("provider").GetString()!,
                    item.GetProperty("boundary").GetString()!, item.GetProperty("featureBehaviors").Deserialize<string[]>()!, []) { TargetResource = scalar ? resource.GetString() : null, TargetResources = targets, FixedBindings = item.TryGetProperty("fixedBindings", out var bindings)
                        ? bindings.Deserialize<DataExecutionFixedBinding[]>(options)! : [] };
            }
            if (item.GetProperty("provider").GetString() is { } provider && provider.StartsWith("standardCrud.", StringComparison.Ordinal))
            {
                if (item.EnumerateObject().Any(property => property.Name is not ("key" or "moduleId" or "provider" or "boundary" or "featureBehaviors" or "resourceKey" or "fixedBindings"))
                    || item.GetProperty("moduleId").GetString() != moduleId
                    || item.TryGetProperty("fixedBindings", out _) && provider is not ("standardCrud.create" or "standardCrud.update" or "standardCrud.delete"))
                    throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
                return new DataExecutionContract(item.GetProperty("key").GetString()!, moduleId, provider,
                    item.GetProperty("boundary").GetString()!, item.GetProperty("featureBehaviors").Deserialize<string[]>()!,
                    StandardCrudRequirements(provider, item.GetProperty("resourceKey").GetString()!))
                { FixedBindings = item.TryGetProperty("fixedBindings", out var fixedBindings) ? fixedBindings.Deserialize<DataExecutionFixedBinding[]>(options)! : [] };
            }
            var contract = item.Deserialize<DataExecutionContract>(options)
                ?? throw new InvalidOperationException("DATA_EXECUTION_CONTRACT_INVALID");
            if (contract.ModuleId != moduleId) throw new InvalidOperationException("DATA_EXECUTION_OWNER_INVALID");
            return contract;
        }).ToArray();
    }

    /// <summary>验证有限合同结构与必要动作，返回全局唯一执行目录。</summary>
    public static IReadOnlyDictionary<string, DataExecutionContract> Compile(IEnumerable<DataExecutionContract> source,
        IReadOnlyList<DataActionDeclaration> actions)
    {
        var contracts = source.ToArray();
        if (contracts.GroupBy(contract => contract.Key, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidOperationException("DATA_EXECUTION_IDENTITY_CONFLICT");
        foreach (var contract in contracts)
        {
            if (string.IsNullOrWhiteSpace(contract.Key) || string.IsNullOrWhiteSpace(contract.ModuleId)
                || string.IsNullOrWhiteSpace(contract.Provider) || contract.Boundary is not ("entry" or "api" or "internal")
                || contract.FeatureBehaviors is null || contract.Requirements is not { Count: > 0 }
                || contract.Branches.Distinct(StringComparer.Ordinal).Count() != contract.Branches.Count
                || contract.Branches.Any(string.IsNullOrWhiteSpace)
                || contract.Branches.Any(branch => !contract.Requirements.Any(requirement => requirement.Branch == branch))
                || contract.Requirements.Any(requirement => string.IsNullOrWhiteSpace(requirement.Role)
                    || requirement.Stage is not ("read" or "prepare" or "submit")
                    || requirement.Shape is not ("object" or "objects" or "query" or "candidate")
                    || requirement.Resources is not { Count: > 0 } || requirement.AnyOf is not { Count: > 0 }
                    || requirement.MinimumCount < 0 || requirement.MaximumCount < requirement.MinimumCount
                    || requirement.Branch is not null && !contract.Branches.Contains(requirement.Branch, StringComparer.Ordinal)
                    || requirement.ReadResolverKey is not null && (string.IsNullOrWhiteSpace(requirement.ReadResolverKey)
                        || requirement.AnyOf.Any(branch => !branch.Contains("read", StringComparer.Ordinal)))
                    || requirement.ResourceAnyOf is not null && (requirement.ResourceAnyOf.Count != requirement.Resources.Count
                        || requirement.AnyOf.Any(branch => !branch.Contains("read", StringComparer.Ordinal))
                        || requirement.Resources.Any(resource => !requirement.ResourceAnyOf.ContainsKey(resource))
                        || requirement.ResourceAnyOf.Values.Any(branches => branches.Count == 0 || branches.Any(branch => branch.Count == 0
                            || branch.Distinct(StringComparer.Ordinal).Count() != branch.Count
                            || branch.Any(key => key == "read" || !actions.Any(action => action.Key == key)))))
                    || requirement.Shape is "candidate" or "object" && (requirement.MinimumCount != 1 || requirement.MaximumCount != 1)
                    || requirement.AnyOf.Any(branch => branch.Count == 0 || branch.Distinct(StringComparer.Ordinal).Count() != branch.Count
                        || branch.Any(key => !actions.Any(action => action.Key == key))))
                || contract.AllowedAdditionalActions.Any(key => !actions.Any(action => action.Key == key)))
                throw new InvalidOperationException("DATA_EXECUTION_CONTRACT_INVALID");
            if (contract.Boundary == "entry" && contract.FixedBindings.Count != 0
                || contract.Boundary != "entry" && (contract.FeatureBehaviors.Count != 0 || contract.FixedBindings.Count == 0)
                || contract.FixedBindings.Any(binding => binding.Kind != contract.Boundary || string.IsNullOrWhiteSpace(binding.Identity)
                    || binding.Kind == "api" && (binding.HttpMethod is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE")
                        || binding.Route?.StartsWith("/", StringComparison.Ordinal) != true || binding.Caller is not null)
                    || binding.Kind == "internal" && (string.IsNullOrWhiteSpace(binding.Caller) || binding.HttpMethod is not null || binding.Route is not null)))
                throw new InvalidOperationException("DATA_EXECUTION_FIXED_BINDING_INVALID");
            if (contract.Provider.StartsWith("standardCrud.", StringComparison.Ordinal)
                && (contract.LegacyActions || contract.Requirements[0].Resources.Count != 1
                    || JsonSerializer.Serialize(contract.Requirements) != JsonSerializer.Serialize(StandardCrudRequirements(contract.Provider, contract.Requirements[0].Resources[0]))))
                throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
            if ((contract.Provider is "entityOwnership.transfer" or "entityOwnership.initialize")
                && (contract.LegacyActions || contract.AllowedAdditionalActions.Count != 0 || contract.Branches.Count != 0
                    || contract.TargetResource is not null || contract.TargetResources is not null || contract.ProviderModuleId is not null
                    || JsonSerializer.Serialize(contract.Requirements) != JsonSerializer.Serialize(OwnershipRequirements(contract.Provider, contract.Requirements[0].Resources))
                    || !actions.Any(action => action.Key == "edit" && action.RequiresRead)))
                throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
        }
        return contracts.ToDictionary(contract => contract.Key, StringComparer.Ordinal);
    }

    // 固定中央能力不归某个业务backend；模块不能注册同名provider替换共享下限。
    internal static bool IsBuiltInProvider(string provider) => provider.StartsWith("standardCrud.", StringComparison.Ordinal)
        || provider is "entityOwnership.transfer" or "entityOwnership.initialize";

    // 转交沿原edit读取依赖；维护保留原显式read404、edit403两个位置，不合并或新增读取。
    private static IReadOnlyList<DataExecutionRequirement> OwnershipRequirements(string provider, IReadOnlyList<string> resources)
    {
        if (resources.Count == 0 || resources.Any(string.IsNullOrWhiteSpace) || resources.Distinct(StringComparer.Ordinal).Count() != resources.Count)
            throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
        return provider switch
        {
            "entityOwnership.transfer" => [new("target", "prepare", "objects", resources, [["edit"]], 1, null),
                new("target", "submit", "objects", resources, [["edit"]], 1, null)],
            "entityOwnership.initialize" => [new("query", "read", "query", resources, [["read"]], 0, null),
                new("visibility", "prepare", "objects", resources, [["read"]], 1, null),
                new("target", "prepare", "objects", resources, [["edit"]], 1, null),
                new("visibility", "submit", "objects", resources, [["read"]], 1, null),
                new("target", "submit", "objects", resources, [["edit"]], 1, null)],
            _ => throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID")
        };
    }

    /// <summary>模板与内建读取注册复用同一固定下限，调用方仅提供真实owner、资源和既有list/read操作。</summary>
    public static DataExecutionContract StandardRead(string moduleId, string resource, string operation)
    {
        if (operation is not ("list" or "read")) throw new ArgumentException("Only existing standard read operations are supported.", nameof(operation));
        var provider = "standardCrud." + operation;
        return new(resource + "." + operation, moduleId, provider, "entry", [resource + ".read"], StandardCrudRequirements(provider, resource));
    }

    // 标准CRUD执行提供者只在此定义必要权限；绑定声明没有覆盖它的字段。
    private static IReadOnlyList<DataExecutionRequirement> StandardCrudRequirements(string provider, string resource) => provider switch
    {
        "standardCrud.create" => [new("candidate", "submit", "candidate", [resource], [["create"]])],
        "standardCrud.update" => [new("target", "prepare", "object", [resource], [["read"]]), new("target", "submit", "object", [resource], [["read", "edit"]])],
        "standardCrud.delete" => [new("target", "submit", "object", [resource], [["read", "delete"]])],
        "standardCrud.read" => [new("target", "read", "object", [resource], [["read"]])],
        "standardCrud.list" => [new("query", "read", "query", [resource], [["read"]], 0, null)],
        "standardCrud.export" => [new("query", "read", "query", [resource], [["read"]], 0, null)],
        "standardCrud.options" => [new("query", "read", "query", [resource], [["read"]], 0, null), new("current", "read", "objects", [resource], [["read"]], 0, null)],
        _ => throw new InvalidOperationException("DATA_EXECUTION_PROVIDER_UNKNOWN")
    };
}
