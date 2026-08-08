namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 声明业务 endpoint 的模块归属，供 Host 启动期校验权限所有权。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class EndpointModuleAttribute : Attribute
{
    public EndpointModuleAttribute(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ModuleId = moduleId.Trim();
    }

    public string ModuleId { get; }
}
