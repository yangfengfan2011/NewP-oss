using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Reflection;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.PluginPackages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class PluginOperationsControllerTests : IDisposable
{
    private readonly string _installRoot = Path.Combine(
        Path.GetTempPath(),
        "plugin_ops_controller_" + Guid.NewGuid().ToString("N"));

    public PluginOperationsControllerTests()
    {
        Directory.CreateDirectory(_installRoot);
    }

    [Fact]
    public void Controller_RequiresAdminPolicy()
    {
        var authorize = typeof(PluginOperationsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("AdminOnly", authorize!.Policy);
    }

    [Fact]
    public void Constructor_DoesNotAcceptRuntimeMutationServices()
    {
        var constructor = Assert.Single(typeof(PluginOperationsController).GetConstructors());

        Assert.Equal(
            [typeof(PluginOperationsAdminService)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void RuntimeWriteActions_DoNotBindMutationPayloadsOrUploadLimits()
    {
        var enable = typeof(PluginOperationsController).GetMethod(nameof(PluginOperationsController.Enable));
        var uploadVerify = typeof(PluginOperationsController).GetMethod(nameof(PluginOperationsController.UploadVerify));

        Assert.NotNull(enable);
        Assert.DoesNotContain(
            enable!.GetParameters(),
            parameter => parameter.ParameterType == typeof(PluginEnableRequest));
        Assert.NotNull(uploadVerify);
        Assert.Null(uploadVerify!.GetCustomAttribute<RequestSizeLimitAttribute>());
    }

    [Fact]
    public void GetModules_RemainsReadOnlyDeploymentObservation()
    {
        var controller = CreateController();

        var result = Assert.IsType<OkObjectResult>(controller.GetModules());

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<PluginModuleStatusDto>>(result.Value));
    }

    [Fact]
    public void GetModule_Returns404_WhenDeploymentDoesNotContainModule()
    {
        var controller = CreateController();

        Assert.IsType<NotFoundObjectResult>(controller.GetModule("missing"));
    }

    [Theory]
    [InlineData("upload-verify")]
    [InlineData("install")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("rollback")]
    [InlineData("uninstall")]
    public void RuntimePluginWriteRoutes_ReturnStableUnsupportedProblemWithoutChangingState(
        string operation)
    {
        var controller = CreateController();
        var stateBefore = CaptureState();

        var action = operation switch
        {
            "upload-verify" => controller.UploadVerify(),
            "install" => controller.Install("upload-1"),
            "enable" => controller.Enable("Audit"),
            "disable" => controller.Disable("Audit"),
            "rollback" => controller.Rollback("Audit"),
            "uninstall" => controller.Uninstall("Audit"),
            _ => throw new InvalidOperationException($"Unsupported test operation '{operation}'.")
        };

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(501, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(501, problem.Status);
        Assert.Equal(
            "PLUGIN_RUNTIME_OPERATIONS_UNSUPPORTED",
            problem.Extensions["code"]);
        Assert.Equal(stateBefore, CaptureState());
    }

    [Fact]
    public async Task RuntimePluginWriteRoutes_ReturnStableUnsupportedProblemThroughHttpPipeline()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(PluginOperationsController).Assembly);
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                static _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("LocalAdmin")));
        builder.Services.AddSingleton(new InstalledPluginStateStore(_installRoot));
        builder.Services.AddSingleton(new ICP.Platform.PluginPlatform.Lifecycle.PluginLifecycleManager(
            new ICP.Platform.PluginPlatform.Lifecycle.PluginLifecycleStateStore(_installRoot, new PluginLifecycleTestDeployment())));
        builder.Services.AddSingleton<PluginOperationsAdminService>();

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/plugin-ops/modules/Audit/disable", content: null)).StatusCode);
        using (var ordinaryRequest = CreateRequest(HttpMethod.Post, "/api/plugin-ops/modules/Audit/disable", isAdmin: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(ordinaryRequest)).StatusCode);
        }

        var routes = new[]
        {
            (HttpMethod.Post, "/api/plugin-ops/uploads/verify"),
            (HttpMethod.Post, "/api/plugin-ops/uploads/upload-1/install"),
            (HttpMethod.Post, "/api/plugin-ops/modules/Audit/enable"),
            (HttpMethod.Post, "/api/plugin-ops/modules/Audit/disable"),
            (HttpMethod.Post, "/api/plugin-ops/modules/Audit/rollback"),
            (HttpMethod.Delete, "/api/plugin-ops/modules/Audit")
        };
        var stateBefore = CaptureState();
        foreach (var (method, route) in routes)
        {
            using var request = CreateRequest(method, route, isAdmin: true);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "PLUGIN_RUNTIME_OPERATIONS_UNSUPPORTED",
                problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(stateBefore, CaptureState());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot))
        {
            Directory.Delete(_installRoot, recursive: true);
        }
    }

    private PluginOperationsController CreateController()
    {
        var stateStore = new InstalledPluginStateStore(_installRoot);
        var manager = new ICP.Platform.PluginPlatform.Lifecycle.PluginLifecycleManager(
            new ICP.Platform.PluginPlatform.Lifecycle.PluginLifecycleStateStore(_installRoot, new PluginLifecycleTestDeployment()));
        return new PluginOperationsController(new PluginOperationsAdminService(stateStore, manager));
    }

    private IReadOnlyList<string> CaptureState()
    {
        return Directory
            .EnumerateFiles(_installRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => $"{Path.GetRelativePath(_installRoot, path)}:{Convert.ToHexString(File.ReadAllBytes(path))}")
            .ToArray();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string route, bool isAdmin)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add("X-Test-User", "plugin-test-user");
        if (isAdmin)
        {
            request.Headers.Add("X-Test-Role", "LocalAdmin");
        }

        return request;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "PluginOperationsTests";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var accountEid))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, accountEid.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
