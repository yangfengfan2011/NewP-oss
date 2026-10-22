using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ICP.Platform.HostApi.Endpoints;

public static class PluginManifestEndpointRouteBuilderExtensions
{
    /// <summary>
    /// 映射明确公开的运行时 manifest，并让它们进入 Host endpoint 覆盖审计。
    /// </summary>
    public static IEndpointRouteBuilder MapPluginManifestEndpoints(
        this IEndpointRouteBuilder endpoints,
        string pluginInstallRoot)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginInstallRoot);

        MapManifest(
            endpoints,
            "/plugins/active-module-runtime.json",
            Path.Combine(pluginInstallRoot, "runtime", "active-module-runtime.json"));
        MapManifest(
            endpoints,
            "/plugins/frontend-plugin-manifest.json",
            Path.Combine(pluginInstallRoot, "runtime", "frontend-plugin-manifest.json"));
        MapManifest(
            endpoints,
            "/plugins/localization-manifest.json",
            Path.Combine(pluginInstallRoot, "runtime", "localization-manifest.json"));
        MapManifest(
            endpoints,
            "/plugins/metadata-runtime-manifest.json",
            Path.Combine(pluginInstallRoot, "runtime", "metadata-runtime-manifest.json"));

        return endpoints;
    }

    private static void MapManifest(
        IEndpointRouteBuilder endpoints,
        string route,
        string manifestPath)
    {
        endpoints.MapGet(route, async (HttpContext context) =>
            {
                if (!File.Exists(manifestPath))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "application/json";
                await context.Response.SendFileAsync(manifestPath);
            })
            .AllowAnonymous();
    }
}
