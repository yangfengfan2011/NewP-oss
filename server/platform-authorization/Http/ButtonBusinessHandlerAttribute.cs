namespace ICP.Platform.Authorization.Http;

/// <summary>绑定真实端点与处理器类型；业务身份复用端点功能或资源拥有者绑定，不重复配置对象动作。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ButtonBusinessHandlerAttribute(Type handlerType, string? operationKey = null) : Attribute
{
    public Type HandlerType { get; } = handlerType;
    /// <summary>资源端点的实际实现操作；缺省沿用原功能绑定，不声明权限动作。</summary>
    public string? OperationKey { get; } = operationKey;
}
