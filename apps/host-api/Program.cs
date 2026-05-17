using System.Reflection;
using System.Text.Json.Serialization;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.Extensions;
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Kernel.PluginDiscovery;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Modules.MetadataConfigCenter;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
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
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("LocalAdmin"));
});
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
builder.Services.AddPlatformPersistence(builder.Configuration);

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
builder.Services.AddSingleton(stateStore);
builder.Services.AddSingleton<PluginOperationsAdminService>();
builder.Services.AddSingleton(sp =>
    new PluginUploadService(
        sp.GetRequiredService<InstalledPluginStateStore>(),
        pluginInstallRoot));
builder.Services.AddSingleton<UnifiedPluginPackageInstaller>();
builder.Services.AddSingleton<UnifiedPluginPackageOperations>();
builder.Services.AddSingleton(sp =>
    new RuntimeViewMaterializer(sp.GetRequiredService<InstalledPluginStateStore>()));
builder.Services.AddSingleton(sp =>
    new PluginOperationsLifecycleService(
        sp.GetRequiredService<InstalledPluginStateStore>(),
        sp.GetRequiredService<UnifiedPluginPackageInstaller>(),
        sp.GetRequiredService<UnifiedPluginPackageOperations>(),
        sp.GetRequiredService<RuntimeViewMaterializer>(),
        sp.GetRequiredService<PluginUploadService>(),
        pluginInstallRoot));
var frontendPluginBaseUrl = builder.Configuration["Platform:FrontendPluginBaseUrl"] ?? "/plugin-assets";
var manifestBuilder = new FrontendRuntimeManifestBuilder(frontendPluginBaseUrl);
var materializer = new RuntimeViewMaterializer(stateStore, manifestBuilder);
materializer.Materialize(pluginInstallRoot);
var runtimeRootPath = Path.Combine(pluginInstallRoot, "runtime");
ProjectBuiltInModuleMetadata(runtimeRootPath, modulesRootPath);
WriteLocalizationRuntimeManifest(runtimeRootPath);
WriteMetadataRuntimeManifest(runtimeRootPath);

var backendLoadViewPath = Path.Combine(pluginInstallRoot, "runtime", "backend-load-view");
var pluginDiscoveryRoot = Directory.Exists(backendLoadViewPath) && Directory.EnumerateFileSystemEntries(backendLoadViewPath).Any()
    ? backendLoadViewPath
    : modulesRootPath;
var pluginAssemblyLoader = new PluginRuntimeAssemblyLoader(hostVersion, disabledPlugins);
var pluginLoadResult = pluginAssemblyLoader.LoadAssemblies(pluginDiscoveryRoot);

builder.Services.AddModuleSystem(options =>
{
    options.Assemblies.Add(Assembly.GetExecutingAssembly());
    options.Assemblies.Add(typeof(MetadataConfigCenterModule).Assembly);
    foreach (var pluginAssembly in pluginLoadResult.LoadedAssemblies)
    {
        options.Assemblies.Add(pluginAssembly);
    }
});

var app = builder.Build();

await app.ApplyPlatformPersistenceAsync();

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

app.Map("/plugins/localization-manifest.json", manifestApp =>
{
    manifestApp.Run(async context =>
    {
        var manifestPath = Path.Combine(pluginInstallRoot, "runtime", "localization-manifest.json");
        if (!File.Exists(manifestPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.SendFileAsync(manifestPath);
    });
});

app.Map("/plugins/metadata-runtime-manifest.json", manifestApp =>
{
    manifestApp.Run(async context =>
    {
        var manifestPath = Path.Combine(pluginInstallRoot, "runtime", "metadata-runtime-manifest.json");
        if (!File.Exists(manifestPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.SendFileAsync(manifestPath);
    });
});

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
app.UseAuditMiddleware();
app.UseAuthorization();
app.MapControllers();
if (app.Services.GetService<IStandardCrudResourceRegistry>() is not null)
{
    app.MapStandardCrudResources();
}

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

static void ProjectBuiltInModuleMetadata(string runtimeRootPath, string modulesRootPath)
{
    if (!Directory.Exists(modulesRootPath))
    {
        return;
    }

    var runtimeModulesPath = Path.Combine(runtimeRootPath, "modules");
    var backendLoadViewPath = Path.Combine(runtimeRootPath, "backend-load-view");
    var pluginOwnedModuleIds = Directory.Exists(backendLoadViewPath)
        ? Directory.GetDirectories(backendLoadViewPath)
            .Select(Path.GetFileName)
            .Where(moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var modulePath in Directory.GetDirectories(modulesRootPath))
    {
        var moduleId = Path.GetFileName(modulePath);
        if (string.IsNullOrWhiteSpace(moduleId) || pluginOwnedModuleIds.Contains(moduleId))
        {
            continue;
        }

        var sourceMetadataPath = Path.Combine(modulePath, "metadata");
        if (!Directory.Exists(sourceMetadataPath))
        {
            continue;
        }

        var targetMetadataPath = Path.Combine(runtimeModulesPath, moduleId, "metadata");
        // 内建模块的 runtime metadata 需要每次启动都与仓库源目录重新对齐，
        // 否则 runtime 目录里一旦残留旧投影，就会继续漏掉后来新增的词典或视图定义。
        if (Directory.Exists(targetMetadataPath))
        {
            Directory.Delete(targetMetadataPath, recursive: true);
        }

        // 内建模块只作为无插件接管时的 metadata runtime 回退；插件已接管的模块不会被这里覆盖。
        CopyDirectory(sourceMetadataPath, targetMetadataPath);
    }
}

static void WriteMetadataRuntimeManifest(string runtimeRootPath)
{
    var runtimeModulesPath = Path.Combine(runtimeRootPath, "modules");
    var moduleOrder = Directory.Exists(runtimeModulesPath)
        ? Directory.GetDirectories(runtimeModulesPath)
            .Select(Path.GetFileName)
            .Where(moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Cast<string>()
            .OrderBy(moduleId => moduleId, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        : Array.Empty<string>();

    File.WriteAllText(
        Path.Combine(runtimeRootPath, "metadata-runtime-manifest.json"),
        new MetadataRuntimeManifestBuilder().Build(runtimeRootPath, moduleOrder));
}

static void WriteLocalizationRuntimeManifest(string runtimeRootPath)
{
    var runtimeModulesPath = Path.Combine(runtimeRootPath, "modules");
    var moduleOrder = Directory.Exists(runtimeModulesPath)
        ? Directory.GetDirectories(runtimeModulesPath)
            .Select(Path.GetFileName)
            .Where(moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Cast<string>()
            .OrderBy(moduleId => moduleId, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        : Array.Empty<string>();

    File.WriteAllText(
        Path.Combine(runtimeRootPath, "localization-manifest.json"),
        new LocalizationRuntimeManifestBuilder().Build(runtimeRootPath, moduleOrder));
}

static void CopyDirectory(string sourceDirectory, string destinationDirectory)
{
    Directory.CreateDirectory(destinationDirectory);

    foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, directory);
        Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
    }

    foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, file);
        var destinationPath = Path.Combine(destinationDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(file, destinationPath, overwrite: false);
    }
}


