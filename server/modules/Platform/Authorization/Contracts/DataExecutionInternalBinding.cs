namespace ICP.Platform.Authorization.Contracts;

/// <summary>内部对象执行的固定来源；调用者来自服务端装配，不接受请求头或载荷自报。</summary>
public sealed record DataExecutionInternalBinding(string Execution, string Provider, Type Caller, string Member)
{
    public string CallerIdentity => Caller.FullName + "." + Member;
}
