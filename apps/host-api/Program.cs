using System.Reflection;
using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Planning;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Antiforgery;
using ICP.Platform.HostApi.Acceptance;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.HostApi.Endpoints;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.IdentityMaintenance;
using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Kernel.Extensions;
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Persistence;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

var identityMaintenanceCommand = IdentityMaintenanceCommandLine.Parse(args);
var identityMaintenanceTerminal = new SystemIdentityMaintenanceTerminal();
if (identityMaintenanceCommand.IsMaintenanceCommand)
{
    if (identityMaintenanceCommand.FailureCode is not null)
    {
        identityMaintenanceTerminal.WriteError(identityMaintenanceCommand.FailureCode);
        Environment.ExitCode = 2;
        return;
    }

    // 交互性在创建 Host builder 和接触持久化前裁决，重定向场景不能产生 schema 或业务写入。
    if (!identityMaintenanceTerminal.IsInteractive)
    {
        identityMaintenanceTerminal.WriteError(IdentityMaintenanceCommandFailureCodes.InteractiveTerminalRequired);
        Environment.ExitCode = 2;
        return;
    }
}

var builder = WebApplication.CreateBuilder(
    identityMaintenanceCommand.IsMaintenanceCommand ? Array.Empty<string>() : args);

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
builder.Services.AddHostAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHostAntiforgery(builder.Environment);
builder.Services.AddSingleton<IIdentityMaintenanceTerminal>(identityMaintenanceTerminal);
builder.Services.AddScoped<IdentityMaintenanceCommandRunner>();

var configuredModulesPath = builder.Configuration["Platform:ModulesPath"] ?? "modules";
var modulesRootPath = Path.IsPathRooted(configuredModulesPath)
    ? configuredModulesPath
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredModulesPath));

var configuredPluginInstallRoot = builder.Configuration["Platform:PluginInstallRoot"] ?? "plugins";
var pluginInstallRoot = Path.IsPathRooted(configuredPluginInstallRoot)
    ? configuredPluginInstallRoot
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPluginInstallRoot));
var externalPluginsEnabled = builder.Configuration.GetValue<bool>("Platform:ExternalPluginsEnabled");
// 关闭态使用独立生命周期状态，避免 built-ins-only 快照覆盖未来重新启用时要恢复的 desired/active/LKG。
var pluginLifecycleRoot = externalPluginsEnabled
    ? pluginInstallRoot
    : Path.Combine(pluginInstallRoot, ".feature-gates", "external-plugins-disabled");
var hostVersion = builder.Configuration["Platform:HostVersion"]
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
    ?? "1.0.0";
var frontendPluginBaseUrl = builder.Configuration["Platform:FrontendPluginBaseUrl"] ?? "/plugin-assets";

var stateStore = new InstalledPluginStateStore(pluginInstallRoot);
var lifecycleManager = new PluginLifecycleManager(new PluginLifecycleStateStore(pluginLifecycleRoot));
var platformApiCatalog = new PlatformApiHostCatalog(
    PlatformApiReleaseIdentity.Version,
    [PlatformApiReleaseIdentity.StandardDataProfile],
    [new PlatformApiReleaseReference(
        PlatformApiReleaseIdentity.Version,
        PlatformApiReleaseIdentity.ReleaseDigest)]);
var standardDataInspector = new StandardDataPackageInspector(platformApiCatalog);
var standardDataInstaller = new StandardDataPackageInstaller(
    pluginInstallRoot,
    stateStore,
    standardDataInspector);
var manifestBuilder = new FrontendRuntimeManifestBuilder(frontendPluginBaseUrl);
var materializer = new RuntimeViewMaterializer(stateStore, manifestBuilder, modulesRootPath);

builder.Services.AddSingleton(stateStore);
builder.Services.AddSingleton(lifecycleManager);
builder.Services.AddSingleton(standardDataInspector);
builder.Services.AddSingleton(standardDataInstaller);
builder.Services.AddSingleton(materializer);
builder.Services.AddSingleton<PluginOperationsAdminService>();
builder.Services.AddSingleton(sp =>
    new PluginUploadService(
        sp.GetRequiredService<InstalledPluginStateStore>(),
        pluginInstallRoot));
builder.Services.AddSingleton<UnifiedPluginPackageInstaller>();
builder.Services.AddSingleton(sp =>
    new PluginOperationsLifecycleService(
        sp.GetRequiredService<InstalledPluginStateStore>(),
        sp.GetRequiredService<UnifiedPluginPackageInstaller>(),
        sp.GetRequiredService<StandardDataPackageInstaller>(),
        sp.GetRequiredService<PluginLifecycleManager>(),
        sp.GetRequiredService<PluginUploadService>(),
        pluginInstallRoot));

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

var pluginInventory = externalPluginsEnabled
    ? InstalledPluginInventoryAdapter.Create(stateStore)
    : Array.Empty<InstalledPluginInventoryItem>();
var activationGate = new PluginActivationGate(
    lifecycleManager,
    new StandardDataPackagePreflight(standardDataInspector, hostVersion),
    new StandardDataCandidatePlanCompiler(
        modulesRootPath,
        standardDataInspector,
        externalPluginsEnabled ? null : ["PluginOps"]),
    new HostPluginPermissionCatalogValidator(),
    new HostPluginPersistencePlanCompiler(storageTypeMapPath, persistenceProvider),
    new HostPluginRuntimeProjection(materializer, pluginInstallRoot));
using var activationSession = activationGate.Prepare(pluginInventory);
var canonicalModulePlan = activationSession.ModulePlan;

// Build 前所有持久化服务都绑定到激活会话编译出的同一个计划实例。
builder.Services.AddPlatformPersistence(builder.Configuration, activationSession.PersistencePlan);
builder.Services.AddRequiredBuiltinReadiness(canonicalModulePlan, activationSession.PersistencePlan);

var standardDataDescriptors = new StandardDataCrudDescriptorProjector().Project(canonicalModulePlan);
if (standardDataDescriptors.Count > 0)
{
    builder.Services.AddStandardCrudKernel();
    foreach (var descriptor in standardDataDescriptors)
    {
        builder.Services.AddStandardCrudResource(descriptor);
    }
}

var activeModuleAssemblies = new CanonicalModuleAssemblyLoader(AppContext.BaseDirectory)
    .Load(canonicalModulePlan);
var activeControllerAssemblies = new[]
    {
        Assembly.GetExecutingAssembly()
    }
    .Concat(activeModuleAssemblies)
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
    foreach (var assembly in activeModuleAssemblies)
    {
        options.Assemblies.Add(assembly);
    }

    foreach (var module in canonicalModulePlan.Modules.Where(static module =>
                 string.Equals(module.FrontendBindingMode, "standard-data", StringComparison.Ordinal)))
    {
        options.ProjectedModules.Add(new ProjectedStandardDataModule(module));
    }
});
builder.Services.AddScoped<IReadinessContributor, IdentityLocalAdministratorReadinessContributor>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Acceptance"))
{
    app.UseHttpsRedirection();
}

if (!externalPluginsEnabled)
{
    // 关闭态在统一 HTTP seam 上拒绝全部插件运维能力，避免只隐藏 Shell 页面后仍可直接调用写入口。
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments(
                "/api/plugin-ops",
                StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    });
}

app.MapPluginManifestEndpoints(pluginInstallRoot);

var registeredServices = app.Services.GetService<IServiceProviderIsService>();
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

var skipAuthorizationCatalogInitialization = builder.Configuration.GetValue<bool>("Platform:SkipAuthorizationCatalogInitialization");
var permissionSynchronizer = new HostPluginPermissionSynchronizer(async cancellationToken =>
{
    if (skipAuthorizationCatalogInitialization && !identityMaintenanceCommand.IsMaintenanceCommand)
    {
        return;
    }

    using var scope = app.Services.CreateScope();
    var initializer = scope.ServiceProvider.GetRequiredService<AuthorizationCatalogInitializer>();
    await initializer.InitializeAsync(endpointAccessPlan.ActivePermissions, cancellationToken);
});
await activationSession.CompleteAsync(
    new HostPluginMigrationExecutor(app, activationSession.PersistencePlan),
    permissionSynchronizer);

if (identityMaintenanceCommand.IsMaintenanceCommand)
{
    using var scope = app.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<IdentityMaintenanceCommandRunner>();
    Environment.ExitCode = await runner.ExecuteAsync(identityMaintenanceCommand.Kind);
    return;
}

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

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseHostAntiforgery();
app.UseMiddleware<RestrictedAuthenticationSessionMiddleware>();
if (registeredServices?.IsService(typeof(IAuditService)) is true)
{
    // Audit 插件被跳过时不能留下依赖其 scoped service 的宿主中间件。
    app.UseAuditMiddleware();
}
app.UseAuthorization();

var modules = registry.GetAllModules();
var logger = app.Logger;

logger.LogInformation(
    "Plugin activation snapshot {SnapshotId} ({PlanHash}) committed from {PluginInstallRoot}; host version: {HostVersion}, canonical modules: {ModuleCount}",
    canonicalModulePlan.SnapshotId,
    canonicalModulePlan.PlanHash,
    pluginInstallRoot,
    hostVersion,
    canonicalModulePlan.Modules.Count);

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
