namespace ICP.Platform.Authorization.Contracts;

/// <summary>纯API真实对象执行的服务器固定来源；不开放客户端声明或参与角色选择。</summary>
public sealed record DataExecutionApiObjectExecutionContext(DataExecutionBindingReference Binding, string Identity);
