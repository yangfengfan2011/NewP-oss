namespace ICP.Platform.Authorization.Contracts;

/// <summary>纯外部API的服务端固定查询来源；不来自view声明、HTTP业务JSON或内部调用者身份。</summary>
public sealed record DataExecutionApiQueryContext(DataExecutionBindingReference Binding, string Identity, string Role);
