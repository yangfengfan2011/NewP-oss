using System.Text.Json;
using System.Text.Json.Serialization;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>能力 owner 的固定下限；资源占位只在正式消费者装配时替换，不是客户端策略语言。</summary>
public sealed record DataExecutionProviderContract(string Key, string ModuleId, string BackendBehaviorKey,
    string ConsumerBinding, IReadOnlyList<DataExecutionRequirement> Requirements);

/// <summary>通用能力贡献的读取、活动 owner 校验与资源绑定。</summary>
public static class DataExecutionProviders
{
    /// <summary>严格读取 owner 提供的能力定义，不允许未知字段削弱合同。</summary>
    public static IReadOnlyList<DataExecutionProviderContract> Read(JsonElement capsule, string moduleId)
    {
        if (!capsule.TryGetProperty("dataExecutionProviders", out var providers)) return [];
        var result = providers.Deserialize<DataExecutionProviderContract[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })!;
        if (result.Any(provider => provider.ModuleId != moduleId)) throw new InvalidOperationException("DATA_EXECUTION_PROVIDER_OWNER_INVALID");
        return result;
    }

    /// <summary>能力必须属于活动 backend；消费者仅绑定资源，不能提交另一份必要要求。</summary>
    public static IReadOnlyList<DataExecutionContract> Resolve(IReadOnlyList<DataExecutionContract> executions,
        IReadOnlyList<DataExecutionProviderContract> providers)
    {
        if (providers.GroupBy(provider => provider.Key, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidOperationException("DATA_EXECUTION_PROVIDER_CONFLICT");
        return executions.Select(execution =>
        {
            var provider = providers.SingleOrDefault(provider => provider.Key == execution.Provider);
            if (execution.TargetResource is null && execution.TargetResources is null)
            {
                if (provider is not null || execution.ProviderModuleId is not null)
                    throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
                return execution;
            }
            if (provider is null) throw new InvalidOperationException("DATA_EXECUTION_PROVIDER_UNKNOWN");
            if (execution.Requirements.Count != 0
                || execution.ProviderModuleId is not null || execution.Branches.Count != 0
                || provider.ConsumerBinding switch
                {
                    "resource-owner" => execution.TargetResource is null || execution.TargetResources is not null,
                    "execution-owner" => execution.TargetResource is not null || execution.TargetResources is not { Count: > 0 }
                        || execution.TargetResources.Any(string.IsNullOrWhiteSpace)
                        || execution.TargetResources.Distinct(StringComparer.Ordinal).Count() != execution.TargetResources.Count,
                    _ => true
                }
                || provider.Requirements.Count == 0 || provider.Requirements.Any(requirement => !requirement.Resources.SequenceEqual(["$resource"])))
                throw new InvalidOperationException("DATA_EXECUTION_REQUIREMENT_INVALID");
            return execution with { ProviderModuleId = provider.ModuleId,
                Requirements = provider.Requirements.Select(requirement => requirement with
                { Resources = execution.TargetResources ?? [execution.TargetResource!] }).ToArray() };
        }).ToArray();
    }
}
