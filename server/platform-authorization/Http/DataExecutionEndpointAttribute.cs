namespace ICP.Platform.Authorization.Http;

/// <summary>提供者在真实HTTP执行上登记能力和阶段；不创造业务模块的功能授权位置。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class DataExecutionEndpointAttribute(string provider, string stage) : Attribute
{
    public string Provider { get; } = provider;
    public string Stage { get; } = stage;
}
