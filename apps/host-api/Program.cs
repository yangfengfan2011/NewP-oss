using System.Reflection;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Kernel.Extensions;
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Kernel.PluginDiscovery;
using ICP.Platform.Kernel.PluginPackages;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = builder.Configuration["LocalAuth:CookieName"] ?? "ICP.Platform.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.LoginPath = "/api/auth/login";
        options.LogoutPath = "/api/auth/logout";
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.Configure<LocalAuthOptions>(builder.Configuration.GetSection("LocalAuth"));
builder.Services.AddSingleton<LocalAuthSeeder>();

var configuredModulesPath = builder.Configuration["Platform:ModulesPath"] ?? "modules";
var modulesRootPath = Path.IsPathRooted(configuredModulesPath)
    ? configuredModulesPath
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredModulesPath));

var configuredPluginInstallRoot = builder.Configuration["Platform:PluginInstallRoot"] ?? "plugins";
var pluginInstallRoot = Path.IsPathRooted(configuredPluginInstallRoot)
    ? configuredPluginInstallRoot
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPluginInstallRoot));
var hostVersion = builder.Configuration["Platform:HostVersion"]
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
    ?? "1.0.0";
var disabledPlugins = builder.Configuration.GetSection("Platform:DisabledPlugins").Get<string[]>() ?? Array.Empty<string>();

var stateStore = new InstalledPluginStateStore(pluginInstallRoot);
var frontendPluginBaseUrl = builder.Configuration["Platform:FrontendPluginBaseUrl"] ?? "/plugin-assets";
var manifestBuilder = new FrontendRuntimeManifestBuilder(frontendPluginBaseUrl);
var materializer = new RuntimeViewMaterializer(stateStore, manifestBuilder);
materializer.Materialize(pluginInstallRoot);

var backendLoadViewPath = Path.Combine(pluginInstallRoot, "runtime", "backend-load-view");
var pluginDiscoveryRoot = Directory.Exists(backendLoadViewPath) && Directory.EnumerateFileSystemEntries(backendLoadViewPath).Any()
    ? backendLoadViewPath
    : modulesRootPath;
var pluginAssemblyLoader = new PluginRuntimeAssemblyLoader(hostVersion, disabledPlugins);
var pluginLoadResult = pluginAssemblyLoader.LoadAssemblies(pluginDiscoveryRoot);

builder.Services.AddModuleSystem(options =>
{
    options.Assemblies.Add(Assembly.GetExecutingAssembly());
    foreach (var pluginAssembly in pluginLoadResult.LoadedAssemblies)
    {
        options.Assemblies.Add(pluginAssembly);
    }
});

var app = builder.Build();

var skipLocalAuthSeeding = builder.Configuration.GetValue<bool>("Platform:SkipLocalAuthSeeding");
if (!skipLocalAuthSeeding)
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<LocalAuthSeeder>();
    await seeder.SeedAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.Map("/plugins/frontend-plugin-manifest.json", manifestApp =>
{
    manifestApp.Run(async context =>
    {
        var manifestPath = Path.Combine(pluginInstallRoot, "runtime", "frontend-plugin-manifest.json");
        if (!File.Exists(manifestPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.SendFileAsync(manifestPath);
    });
});

var publicAssetsPath = Path.Combine(pluginInstallRoot, "public");
if (Directory.Exists(publicAssetsPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(publicAssetsPath),
        RequestPath = frontendPluginBaseUrl
    });
}

app.UseAuthentication();
app.UseAuditMiddleware();
app.UseAuthorization();
app.MapControllers();

var registry = app.Services.GetRequiredService<ModuleRegistry>();
var modules = registry.GetAllModules();
var logger = app.Logger;

logger.LogInformation(
    "Plugin discovery root: {PluginInstallRoot}, backend load view: {BackendLoadViewPath}, host version: {HostVersion}, loadable plugins: {LoadedCount}, skipped plugins: {SkippedCount}",
    pluginDiscoveryRoot,
    backendLoadViewPath,
    hostVersion,
    pluginLoadResult.SuccessfulPlugins.Count,
    pluginLoadResult.FailedPlugins.Count);

foreach (var failedPlugin in pluginLoadResult.FailedPlugins)
{
    logger.LogWarning(
        "Skipped plugin package {PluginDirectory}: {ErrorType} - {ErrorMessage}",
        failedPlugin.PluginDirectory ?? "(unknown)",
        failedPlugin.ErrorType,
        failedPlugin.ErrorMessage ?? "No details");
}

logger.LogInformation("=== Loaded Modules ===");
foreach (var (moduleId, module) in modules)
{
    logger.LogInformation("Module: {ModuleId} - {DisplayName} (v{Version})",
        moduleId, module.DisplayName, module.Version);
}

logger.LogInformation("=== Permissions ===");
var permissions = registry.GetAllPermissions();
logger.LogInformation("Loaded {Count} permission descriptors", permissions.Count);

logger.LogInformation("=== Menus ===");
var menus = registry.GetAllMenus();
logger.LogInformation("Loaded {Count} menu items", menus.Count);

app.Run();


