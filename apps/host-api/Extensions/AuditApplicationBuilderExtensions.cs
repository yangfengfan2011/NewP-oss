using ICP.Platform.HostApi.Middleware;
using ICP.Platform.Modules.Audit.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.Extensions;

public static class AuditApplicationBuilderExtensions
{
    public static IApplicationBuilder UseAuditMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuditMiddleware>();
    }

    public static IApplicationBuilder UseAuditMiddlewareIfRegistered(this IApplicationBuilder app)
    {
        var services = app.ApplicationServices.GetService<IServiceProviderIsService>();
        // Audit 插件被跳过时不能留下依赖其 scoped service 的宿主中间件。
        return services?.IsService(typeof(IAuditService)) is true
            ? app.UseAuditMiddleware()
            : app;
    }
}
