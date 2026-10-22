using System.Text.Json.Serialization;

namespace ICP.Platform.Authorization.Contracts;

/// <summary>owner已从真实来源解析的规范ACL目标；关系身份不进入公共授权合同。</summary>
public sealed record DataScopeAuthorizationTarget(string ResourceKey, string ObjectId);

/// <summary>批量输入没有持久化行ID；每个对象的技术身份由服务端拥有。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeBatchSubject(string Type, string? SubjectId, long Priority,
    IReadOnlyDictionary<string, string> Actions, DataScopeMatchOptions? MatchOptions = null, string? ExpiresAt = null);

/// <summary>混合资源分别声明完整主体项，动作只能使用该资源正式采用的key。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeBatchResourceConfiguration(string ResourceKey, IReadOnlyList<DataScopeBatchSubject> Subjects);

/// <summary>U保留未指定选择器并整项替换指定选择器；R替换全部直接项，空替换必须明确确认。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeBatchConfiguration(string Mode, IReadOnlyList<DataScopeBatchResourceConfiguration> Resources,
    bool ConfirmClear = false);

/// <summary>提交后只返回安全计数，不因自身失权再次读取配置。</summary>
public sealed record DataScopeBatchAuthorizationReceipt(bool Succeeded, int Targets, int Changed);
