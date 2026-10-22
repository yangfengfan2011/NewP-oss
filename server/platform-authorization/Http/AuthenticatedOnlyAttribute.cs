using Microsoft.AspNetCore.Authorization;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 显式声明 endpoint 只要求服务器认证，不绑定业务权限。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuthenticatedOnlyAttribute : AuthorizeAttribute
{
}
