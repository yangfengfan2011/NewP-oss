using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>一个有序角色要求；各要求取交集，AnyOf 中的分支可替代，分支内部动作取交集。</summary>
public sealed record DataExecutionRequirement(string Role, string Stage, string Shape,
    IReadOnlyList<string> Resources, IReadOnlyList<IReadOnlyList<string>> AnyOf,
    int MinimumCount = 1, int? MaximumCount = 1);

/// <summary>执行 owner 的固定合同；入口只能引用，不能替换必要要求。</summary>
public sealed record DataExecutionContract(string Key, string ModuleId, string Provider, string Boundary,
    IReadOnlyList<string> FeatureBehaviors, IReadOnlyList<DataExecutionRequirement> Requirements)
{
    public bool LegacyActions { get; init; }
    public IReadOnlyList<string> AllowedAdditionalActions { get; init; } = [];
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
            if (item.GetProperty("provider").GetString() is { } provider && provider.StartsWith("standardCrud.", StringComparison.Ordinal))
            {
                if (item.EnumerateObject().Any(property => property.Name is not ("key" or "moduleId" or "provider" or "boundary" or "featureBehaviors" or "resourceKey"))
                    || item.GetProperty("moduleId").GetString() != moduleId)
                    throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
                return new DataExecutionContract(item.GetProperty("key").GetString()!, moduleId, provider,
                    item.GetProperty("boundary").GetString()!, item.GetProperty("featureBehaviors").Deserialize<string[]>()!,
                    StandardCrudRequirements(provider, item.GetProperty("resourceKey").GetString()!));
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
                || contract.Requirements.Any(requirement => string.IsNullOrWhiteSpace(requirement.Role)
                    || requirement.Stage is not ("read" or "prepare" or "submit")
                    || requirement.Shape is not ("object" or "objects" or "query" or "candidate")
                    || requirement.Resources is not { Count: > 0 } || requirement.AnyOf is not { Count: > 0 }
                    || requirement.MinimumCount < 0 || requirement.MaximumCount < requirement.MinimumCount
                    || requirement.Shape is "candidate" or "object" && (requirement.MinimumCount != 1 || requirement.MaximumCount != 1)
                    || requirement.AnyOf.Any(branch => branch.Count == 0 || branch.Distinct(StringComparer.Ordinal).Count() != branch.Count
                        || branch.Any(key => !actions.Any(action => action.Key == key))))
                || contract.AllowedAdditionalActions.Any(key => !actions.Any(action => action.Key == key)))
                throw new InvalidOperationException("DATA_EXECUTION_CONTRACT_INVALID");
            if (contract.Provider.StartsWith("standardCrud.", StringComparison.Ordinal)
                && (contract.LegacyActions || contract.Requirements[0].Resources.Count != 1
                    || JsonSerializer.Serialize(contract.Requirements) != JsonSerializer.Serialize(StandardCrudRequirements(contract.Provider, contract.Requirements[0].Resources[0]))))
                throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
        }
        return contracts.ToDictionary(contract => contract.Key, StringComparer.Ordinal);
    }

    // 标准CRUD执行提供者只在此定义必要权限；绑定声明没有覆盖它的字段。
    private static IReadOnlyList<DataExecutionRequirement> StandardCrudRequirements(string provider, string resource) => provider switch
    {
        "standardCrud.create" => [new("candidate", "submit", "candidate", [resource], [["create"]])],
        "standardCrud.update" => [new("target", "prepare", "object", [resource], [["read"]]), new("target", "submit", "object", [resource], [["read", "edit"]])],
        "standardCrud.delete" => [new("target", "submit", "object", [resource], [["read", "delete"]])],
        "standardCrud.read" => [new("target", "read", "object", [resource], [["read"]])],
        "standardCrud.list" => [new("query", "read", "query", [resource], [["read"]], 0, null)],
        _ => throw new InvalidOperationException("DATA_EXECUTION_PROVIDER_UNKNOWN")
    };
}
