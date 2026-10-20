namespace ICP.Platform.Authorization.Contracts;

/// <summary>完整管理读取的正式对象与目录参数；操作由真实端点固定，不能从请求选择权限。</summary>
public sealed record DataScopeObjectReadInput(string ResourceKey, string ObjectId, string Operation,
    string? SubjectType = null, string? Query = null, int Page = 1, IReadOnlyList<string>? CurrentIds = null, string? AccountId = null);

/// <summary>管理读取与平台检查共用同一已解析规范对象，不是跨请求允许凭证。</summary>
public abstract record DataScopeObjectReadState(ButtonExecutionTarget Target);

/// <summary>完整配置和管理目录复用原管理资格；纯查看仍有独立读取来源。</summary>
public interface IDataScopeObjectReadHandler
    : IButtonBusinessHandler<DataScopeObjectReadInput, DataScopeObjectReadState, DataScopeAuthorizationResult<object>>
{
}
