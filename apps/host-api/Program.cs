using System.Reflection;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Kernel.Extensions;
using ICP.Platform.Kernel.ModuleLoader;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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

builder.Services.AddModuleSystem(options =>
{
    options.Assemblies.Add(Assembly.GetExecutingAssembly());

    var baseDirectory = AppContext.BaseDirectory;
    var moduleFiles = Directory.GetFiles(baseDirectory, "*Module.dll");

    foreach (var moduleFile in moduleFiles)
    {
        try
        {
            var assembly = Assembly.LoadFrom(moduleFile);
            options.Assemblies.Add(assembly);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to load module assembly {moduleFile}: {ex.Message}");
        }
    }
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<LocalAuthSeeder>();
    await seeder.SeedAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuditMiddleware();
app.UseAuthorization();
app.MapControllers();

var registry = app.Services.GetRequiredService<ModuleRegistry>();
var modules = registry.GetAllModules();
var logger = app.Logger;

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
