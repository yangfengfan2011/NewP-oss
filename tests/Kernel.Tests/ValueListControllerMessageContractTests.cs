using System.Text.Json;
using ICP.Platform.Sdk.UserMessages;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public class ValueListControllerMessageContractTests : IClassFixture<ValueListControllerMessageContractTests.TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ValueListControllerMessageContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_ReturnsStructuredUserMessage_WhenDuplicateCode()
    {
        using var scope = _factory.Services.CreateScope();
        var (controller, dtoType) = CreateController(scope.ServiceProvider);
        var code = "STATUS_ACTIVE_" + Guid.NewGuid().ToString("N");
        var createResult = await InvokeAsync(
            controller,
            "Create",
            CreateDto(dtoType, code, "启用"),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(GetActionResult(createResult));

        var duplicateResult = await InvokeAsync(
            controller,
            "Create",
            CreateDto(dtoType, code, "重复"),
            CancellationToken.None);
        var errorResult = Assert.IsType<ObjectResult>(GetActionResult(duplicateResult));
        Assert.Equal(400, errorResult.StatusCode);
        var message = Assert.IsType<UserMessageDto>(errorResult.Value);

        Assert.False(message.Success);
        Assert.Equal(UserMessageLevel.error, message.Level);
        Assert.Equal("ValueLists.CodeAlreadyExists", message.MessageKey);
        Assert.Equal(code, message.MessageArgs!["code"]);
        Assert.Equal(RenderDictionaryText("CodeAlreadyExists", ("code", code)), message.FallbackMessage);
        Assert.Null(typeof(UserMessageDto).GetProperty("Message"));
    }

    [Fact]
    public async Task Delete_ReturnsStructuredUserMessage_WhenItemIsMissing()
    {
        using var scope = _factory.Services.CreateScope();
        var (controller, _) = CreateController(scope.ServiceProvider);
        const string missingId = "missing-id";
        var deleteResult = await InvokeAsync(
            controller,
            "Delete",
            missingId,
            CancellationToken.None);
        var errorResult = Assert.IsType<ObjectResult>(deleteResult);
        Assert.Equal(404, errorResult.StatusCode);
        var message = Assert.IsType<UserMessageDto>(errorResult.Value);

        Assert.False(message.Success);
        Assert.Equal(UserMessageLevel.error, message.Level);
        Assert.Equal("ValueLists.ItemNotFound", message.MessageKey);
        Assert.Equal(missingId, message.MessageArgs!["id"]);
        Assert.Equal(RenderDictionaryText("ItemNotFound", ("id", missingId)), message.FallbackMessage);
        Assert.Null(typeof(UserMessageDto).GetProperty("Message"));
    }

    public class TestWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
    {
        private readonly string _installRoot;
        private TimeSpan _shutdownTimeout = new HostOptions().ShutdownTimeout;

        public TestWebApplicationFactory()
        {
            _installRoot = Path.Combine(Path.GetTempPath(), $"value_list_contract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_installRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Platform:PluginInstallRoot", _installRoot);
            builder.UseSetting("Files:StorageRoot", Path.Combine(_installRoot, "storage"));
            builder.UseSetting("Platform:SkipAuthorizationCatalogInitialization", "true");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPersistenceMigrationRunner>();
                services.AddSingleton<IPersistenceMigrationRunner, TestPersistenceMigrationRunner>();
                services.RemoveAll<IPersistenceConsistencyChecker>();
                services.AddSingleton<IPersistenceConsistencyChecker, TestPersistenceConsistencyChecker>();
                var repositoryServiceType = services
                    .Select(descriptor => descriptor.ServiceType)
                    .Single(type => string.Equals(
                        type.FullName,
                        "ICP.Platform.Modules.ConfigCenter.Repositories.IValueListRepository",
                        StringComparison.Ordinal));
                var repositoryImplementationType = repositoryServiceType.Assembly.GetType(
                    "ICP.Platform.Modules.ConfigCenter.Repositories.ValueListRepository",
                    throwOnError: true)!;
                services.RemoveAll(repositoryServiceType);
                services.Add(ServiceDescriptor.Singleton(
                    repositoryServiceType,
                    repositoryImplementationType));
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            _shutdownTimeout = host.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout;
            return host;
        }

        public override async ValueTask DisposeAsync()
        {
            using var shutdown = new CancellationTokenSource(_shutdownTimeout);
            await base.DisposeAsync();
            await HostDeploymentRootCleanup.DeleteAsync(_installRoot, shutdown.Token);
        }
    }

    private static (object Controller, Type DtoType) CreateController(IServiceProvider services)
    {
        const string serviceTypeName = "ICP.Platform.Modules.ConfigCenter.Services.IValueListService";
        var serviceTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(serviceTypeName, throwOnError: false))
            .Where(static type => type is not null)
            .Cast<Type>()
            .Where(type => services.GetService(type) is not null)
            .ToArray();
        var serviceType = Assert.Single(serviceTypes);
        var service = services.GetRequiredService(serviceType);
        var controllerType = serviceType.Assembly.GetType(
            "ICP.Platform.Modules.ConfigCenter.Controllers.ValueListController",
            throwOnError: true)!;
        var dtoType = serviceType.Assembly.GetType(
            "ICP.Platform.Modules.ConfigCenter.DTOs.ValueListItemDto",
            throwOnError: true)!;
        return (Activator.CreateInstance(controllerType, service)!, dtoType);
    }

    private static object CreateDto(Type dtoType, string code, string displayName)
    {
        var dto = Activator.CreateInstance(dtoType)!;
        dtoType.GetProperty("Code")!.SetValue(dto, code);
        dtoType.GetProperty("DisplayName")!.SetValue(dto, displayName);
        return dto;
    }

    private static async Task<object> InvokeAsync(
        object target,
        string methodName,
        params object?[] arguments)
    {
        var method = target.GetType().GetMethods()
            .Single(candidate =>
                string.Equals(candidate.Name, methodName, StringComparison.Ordinal)
                && candidate.GetParameters().Length == arguments.Length);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(target, arguments));
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static IActionResult GetActionResult(object actionResult) =>
        Assert.IsAssignableFrom<IActionResult>(
            actionResult.GetType().GetProperty("Result")!.GetValue(actionResult));

    private static string RenderDictionaryText(string entryKey, params (string Key, string Value)[] args)
    {
        var dictionaryPath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "ConfigCenter"), "metadata", "dictionaries", "ValueLists.dic.json");
        using var document = JsonDocument.Parse(File.ReadAllText(dictionaryPath));

        var template = document.RootElement
            .GetProperty("ValueLists")
            .GetProperty(entryKey)
            .GetProperty("zhs")
            .GetString() ?? string.Empty;

        foreach (var (key, value) in args)
        {
            template = template.Replace($"{{{key}}}", value, StringComparison.Ordinal);
        }

        return template;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
