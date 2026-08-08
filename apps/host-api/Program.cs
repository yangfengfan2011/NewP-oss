using System.Reflection;
using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Acceptance;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.HostApi.Endpoints;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Kernel.Extensions;
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Kernel.PluginDiscovery;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Modules.MetadataConfigCenter;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var mvcBuilder = builder.Services.AddControllers(options =>
    {
        // 生成 DTO 同时承载 create/update/response；Id 等服务端生成字段不能因为 C# 非空标注被请求模型绑定强制必填。
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        // 前端与生成 DTO 之间的枚举契约使用字符串值；Host 统一接住，避免各模块重复配置。
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostReadiness();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("LocalAdmin"));
    options.AddPolicy(DenyAllAttribute.PolicyName, policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(static _ => false));
    // 启动审计阻止当前漏标 endpoint；fallback 继续覆盖运行期后来加入的漏网 endpoint。
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(static _ => false)
        .Build();
});
builder.Services.AddPlatformAuthorization();
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
builder.Services.AddLocalAuthSeeding(builder.Configuration);

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
var frontendPluginBaseUrl = builder.Configuration["Platform:FrontendPluginBaseUrl"] ?? "/plugin-assets";

var stateStore = new InstalledPluginStateStore(pluginInstallRoot);
builder.Services.AddSingleton(stateStore);
builder.Services.AddSingleton<PluginOperationsAdminService>();
builder.Services.AddSingleton(sp =>
    new PluginUploadService(
        sp.GetRequiredService<InstalledPluginStateStore>(),
        pluginInstallRoot));
builder.Services.AddSingleton<UnifiedPluginPackageInstaller>();
builder.Services.AddSingleton<UnifiedPluginPackageOperations>();
builder.Services.AddSingleton(sp =>
    new RuntimeViewMaterializer(
        sp.GetRequiredService<InstalledPluginStateStore>(),
        new FrontendRuntimeManifestBuilder(frontendPluginBaseUrl),
        modulesRootPath));
builder.Services.AddSingleton(sp =>
    new PluginOperationsLifecycleService(
        sp.GetRequiredService<InstalledPluginStateStore>(),
        sp.GetRequiredService<UnifiedPluginPackageInstaller>(),
        sp.GetRequiredService<UnifiedPluginPackageOperations>(),
        sp.GetRequiredService<RuntimeViewMaterializer>(),
        sp.GetRequiredService<PluginUploadService>(),
        pluginInstallRoot));
var manifestBuilder = new FrontendRuntimeManifestBuilder(frontendPluginBaseUrl);
var materializer = new RuntimeViewMaterializer(stateStore, manifestBuilder, modulesRootPath);
var canonicalModulePlan = materializer.Materialize(pluginInstallRoot);
var configuredPersistenceProvider = builder.Configuration["Persistence:Provider"];
var persistenceProvider = PersistenceProvider.SqlServer;
if (!string.IsNullOrWhiteSpace(configuredPersistenceProvider)
    && !Enum.TryParse(configuredPersistenceProvider, true, out persistenceProvider))
{
    throw new InvalidOperationException("Persistence provider is missing or unsupported.");
}

var storageTypeMapPath = Path.Combine(
    AppContext.BaseDirectory,
    "server",
    "platform-modeling",
    "metadata",
    "storage-types.json");
var persistenceCompilation = new PersistencePlanCompiler(storageTypeMapPath)
    .Compile(canonicalModulePlan, persistenceProvider);
if (!persistenceCompilation.IsSuccess || persistenceCompilation.Plan is null)
{
    var details = string.Join(
        Environment.NewLine,
        persistenceCompilation.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
    throw new InvalidOperationException($"Compiled Persistence Plan failed.{Environment.NewLine}{details}");
}

builder.Services.AddPlatformPersistence(builder.Configuration, persistenceCompilation.Plan);

var backendLoadViewPath = Path.Combine(pluginInstallRoot, "runtime", "backend-load-view");
var pluginDiscoveryRoot = backendLoadViewPath;
var pluginAssemblyLoader = new PluginRuntimeAssemblyLoader(hostVersion, disabledPlugins);
var pluginLoadResult = pluginAssemblyLoader.LoadAssemblies(pluginDiscoveryRoot);
var activeControllerAssemblies = new[]
    {
        Assembly.GetExecutingAssembly(),
        typeof(MetadataConfigCenterModule).Assembly
    }
    .Concat(pluginLoadResult.LoadedAssemblies)
    .DistinctBy(static assembly => assembly.FullName, StringComparer.Ordinal)
    .ToArray();
mvcBuilder.ConfigureApplicationPartManager(manager =>
{
    // 只让活动模块贡献 Controller；引用但未装载的模块不能绕过 active catalog 进入 endpoint plan。
    manager.ApplicationParts.Clear();
    foreach (var assembly in activeControllerAssemblies)
    {
        manager.ApplicationParts.Add(new AssemblyPart(assembly));
    }
});

builder.Services.AddModuleSystem(options =>
{
    foreach (var assembly in activeControllerAssemblies)
    {
        options.Assemblies.Add(assembly);
    }
});

var app = builder.Build();

await app.ApplyPlatformPersistenceAsync();

app.UseSwagger();
app.UseSwaggerUI();

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Acceptance"))
{
    app.UseHttpsRedirection();
}

app.MapPluginManifestEndpoints(pluginInstallRoot);

var runtimeModulesPath = Path.Combine(pluginInstallRoot, "runtime", "modules");
if (Directory.Exists(runtimeModulesPath))
{
    // 只暴露 runtime metadata 投影目录，避免把 backend-load-view 中的后端装载文件放到公共静态面。
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(runtimeModulesPath),
        RequestPath = "/plugins/runtime/modules"
    });
}

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
var registeredServices = app.Services.GetService<IServiceProviderIsService>();
if (registeredServices?.IsService(typeof(IAuditService)) is true)
{
    // Audit 插件被跳过时不能留下依赖其 scoped service 的宿主中间件。
    app.UseAuditMiddleware();
}
app.UseAuthorization();
app.MapControllers();
app.MapPersonnelAcceptanceFixture(builder.Configuration);
if (app.Services.GetService<IStandardCrudResourceRegistry>() is not null)
{
    app.MapStandardCrudResources();
}

var registry = app.Services.GetRequiredService<ModuleRegistry>();
var endpointAccessPlan = EndpointAccessPlanCompiler.Compile(
    ((IEndpointRouteBuilder)app).DataSources,
    registry);

var skipLocalAuthSeeding = builder.Configuration.GetValue<bool>("Platform:SkipLocalAuthSeeding");
if (!skipLocalAuthSeeding)
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<LocalAuthSeeder>();
    await seeder.SeedAsync(endpointAccessPlan.ActivePermissions);
}

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
