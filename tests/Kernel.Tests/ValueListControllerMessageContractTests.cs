using System.Text.Json;
using ICP.Platform.Modules.ConfigCenter.Controllers;
using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Modules.ConfigCenter.Services;
using ICP.Platform.Sdk.UserMessages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

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
        var controller = new ValueListController(scope.ServiceProvider.GetRequiredService<IValueListService>());
        var code = "STATUS_ACTIVE_" + Guid.NewGuid().ToString("N");
        var createResult = await controller.Create(new ValueListItemDto
        {
            Code = code,
            DisplayName = "启用"
        });
        Assert.IsType<OkObjectResult>(createResult.Result);

        var duplicateResult = await controller.Create(new ValueListItemDto
        {
            Code = code,
            DisplayName = "重复"
        });
        var errorResult = Assert.IsType<ObjectResult>(duplicateResult.Result);
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
        var controller = new ValueListController(scope.ServiceProvider.GetRequiredService<IValueListService>());
        const string missingId = "missing-id";
        var deleteResult = await controller.Delete(missingId);
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

        public TestWebApplicationFactory()
        {
            _installRoot = Path.Combine(Path.GetTempPath(), $"value_list_contract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_installRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Platform:PluginInstallRoot", _installRoot);
            builder.UseSetting("Platform:SkipAuthorizationCatalogInitialization", "true");
            builder.UseSetting("Persistence:AutoMigrate", "false");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IValueListRepository>();
                services.AddSingleton<IValueListRepository, ValueListRepository>();
            });
        }

        public new void Dispose()
        {
            base.Dispose();
            if (Directory.Exists(_installRoot))
            {
                Directory.Delete(_installRoot, recursive: true);
            }
        }
    }

    private static string RenderDictionaryText(string entryKey, params (string Key, string Value)[] args)
    {
        var dictionaryPath = Path.Combine(FindRepositoryRoot(), "modules", "ConfigCenter", "metadata", "dictionaries", "ValueLists.dic.json");
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
