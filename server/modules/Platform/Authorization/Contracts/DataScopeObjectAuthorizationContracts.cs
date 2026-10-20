using System.Text.Json.Serialization;

namespace ICP.Platform.Authorization.Contracts;

/// <summary>真实owner解析对象后提交完整直接配置；请求身份必须与解析出的目标一致。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record DataScopeObjectConfiguration(string ResourceKey, string ObjectId, IReadOnlyList<DataScopeDirectSubjectDto> Subjects);

/// <summary>直接主体的期限为空表示永久；非空输入沿平台 Instant 规范化为 UTC 秒。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeDirectSubjectDto(string Id, string Type, string? SubjectId, long Priority,
    IReadOnlyDictionary<string, string> Actions, DataScopeMatchOptions? MatchOptions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpiresAt = null);

/// <summary>稳定数据管理结果；owner仍负责正式功能位置和实际来源，不能通过参数跳过资格。</summary>
public enum DataScopeAuthorizationStatus { Succeeded, Forbidden, NotFound, InvalidInput }

/// <summary>保留授权模块现有校验原因及参数，HTTP owner使用既有DataScope.InvalidConfiguration错误语义。</summary>
public sealed record DataScopeConfigurationError(string Message, string Code, string ReasonKey,
    IReadOnlyDictionary<string, object?> Args, string? Field = null);

/// <summary>拒绝和输入错误不携带对象内容；基础设施故障和取消继续抛出。</summary>
public record DataScopeAuthorizationResult(DataScopeAuthorizationStatus Status, DataScopeConfigurationError? Error = null);

/// <summary>成功读取的稳定值；不能把内部异常转换为正常拒绝。</summary>
public sealed record DataScopeAuthorizationResult<T>(DataScopeAuthorizationStatus Status, T? Value = default,
    DataScopeConfigurationError? Error = null) : DataScopeAuthorizationResult(Status, Error);

/// <summary>owner入口通过正式功能授权并解析完整来源后，复用对象访问、data-authorize及原事务能力。</summary>
public interface IDataScopeObjectAuthorization
{
    /// <summary>真实owner选择正式管理来源，在原目标资格点执行完整read与authorize。</summary>
    Task<DataScopeAuthorizationStatus> CheckAsync(string resourceKey, string objectId, DataExecutionBindingReference execution,
        CancellationToken token = default);
    /// <summary>按正式来源重验目标后读取完整直接配置，不裁剪为调用者已有业务动作。</summary>
    Task<DataScopeAuthorizationResult<DataScopeObjectConfiguration>> ReadAsync(string resourceKey, string objectId,
        DataExecutionBindingReference execution, CancellationToken token = default);
    /// <summary>正式提交来源传入原锁内替换；成功自身失权后不增加回读。</summary>
    Task<DataScopeAuthorizationResult> ReplaceAsync(string resourceKey, string objectId, DataScopeObjectConfiguration configuration,
        DataExecutionBindingReference execution, CancellationToken token = default);
    /// <summary>配置主体候选沿真实管理来源判权，保留原最小目录及分页。</summary>
    Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> SubjectsAsync(string resourceKey, string objectId, string type,
        string? query, int page, IReadOnlyList<string> currentIds, DataExecutionBindingReference execution,
        CancellationToken token = default);
    /// <summary>共享主体端点明确接受单对象管理和批量编辑的有限来源，资格仍消费同一target下限。</summary>
    Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> SubjectsAsync(string resourceKey, string objectId, string type,
        string? query, int page, IReadOnlyList<string> currentIds, IReadOnlyList<DataExecutionBindingReference> executions,
        CancellationToken token = default);
}
