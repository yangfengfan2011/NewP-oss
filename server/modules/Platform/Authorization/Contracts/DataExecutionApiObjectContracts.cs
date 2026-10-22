namespace ICP.Platform.Authorization.Contracts;

/// <summary>纯固定API的服务端单对象读取来源；真实目标由该API的原有来源解析形成。</summary>
public sealed record DataExecutionApiObjectContext(DataExecutionBindingReference Binding, string Identity, string Role);
