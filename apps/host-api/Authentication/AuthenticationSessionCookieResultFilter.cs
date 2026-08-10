using ICP.Platform.Authentication.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ICP.Platform.HostApi.Authentication;

/// <summary>
/// 把模块成功签发的新会话凭据交给 Host Cookie 集成；失败结果和普通响应不产生 Cookie。
/// </summary>
public sealed class AuthenticationSessionCookieResultFilter : IAsyncResultFilter
{
    private readonly IHostAuthenticationCookie _cookie;

    public AuthenticationSessionCookieResultFilter(IHostAuthenticationCookie cookie)
    {
        _cookie = cookie;
    }

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult
            {
                StatusCode: null or >= 200 and < 300,
                Value: IAuthenticationSessionCookieResponse rotation
            })
        {
            _cookie.Append(
                context.HttpContext,
                rotation.GetSessionCredential(),
                rotation.GetSessionExpiresAtUtc());
        }

        await next();
    }
}
