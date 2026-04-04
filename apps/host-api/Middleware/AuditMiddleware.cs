using System.Diagnostics;
using ICP.Platform.Modules.Audit.Models;
using ICP.Platform.Modules.Audit.Services;

namespace ICP.Platform.HostApi.Middleware;

/// <summary>
/// 审计访问日志中间件
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAuditService auditService)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            var accessLog = new AccessLog
            {
                AccessTime = DateTime.UtcNow,
                UserId = context.User.Identity?.Name,
                UserName = context.User.Identity?.Name,
                RequestPath = context.Request.Path,
                HttpMethod = context.Request.Method,
                StatusCode = context.Response.StatusCode,
                ResponseTime = stopwatch.ElapsedMilliseconds,
                ClientIp = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                RequestParams = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                ExceptionMessage = exception?.Message
            };

            await auditService.LogAccessAsync(accessLog);
        }
    }
}
