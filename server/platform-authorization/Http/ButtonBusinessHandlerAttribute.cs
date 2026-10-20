namespace ICP.Platform.Authorization.Http;

/// <summary>绑定真实端点与处理器类型；业务身份复用端点功能或资源拥有者绑定，不重复配置对象动作。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ButtonBusinessHandlerAttribute(Type handlerType) : Attribute
{
    public Type HandlerType { get; } = handlerType;
}
