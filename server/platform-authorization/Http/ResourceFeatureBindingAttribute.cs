namespace ICP.Platform.Authorization.Http;

/// <summary>通用资源端点按规范资源键绑定其所属模块的功能行为；运行时仍逐请求判权。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ResourceFeatureBindingAttribute(string actionKey) : Attribute
{
    public string ActionKey { get; } = actionKey;
}
