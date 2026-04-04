using ICP.Platform.HostApi.Middleware;

namespace ICP.Platform.HostApi.Extensions;

public static class AuditApplicationBuilderExtensions
{
    public static IApplicationBuilder UseAuditMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuditMiddleware>();
    }
}
