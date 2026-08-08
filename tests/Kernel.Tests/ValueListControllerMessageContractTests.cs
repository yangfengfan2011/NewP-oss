using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Builder;
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
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var code = "STATUS_ACTIVE_" + Guid.NewGuid().ToString("N");

        var createResponse = await client.PostAsJsonAsync("/api/value-lists", new
        {
            code,
            name = "启用"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var duplicateResponse = await client.PostAsJsonAsync("/api/value-lists", new
        {
            code,
            name = "重复"
        });

        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

        using var document = JsonDocument.Parse(await duplicateResponse.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("error", root.GetProperty("level").GetString());
        Assert.Equal("ValueLists.CodeAlreadyExists", root.GetProperty("messageKey").GetString());
        Assert.Equal(code, root.GetProperty("messageArgs").GetProperty("code").GetString());
        Assert.Equal(RenderDictionaryText("CodeAlreadyExists", ("code", code)), root.GetProperty("fallbackMessage").GetString());
        Assert.False(root.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Delete_ReturnsStructuredUserMessage_WhenItemIsMissing()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        const string missingId = "missing-id";
        var deleteResponse = await client.DeleteAsync($"/api/value-lists/{missingId}");

        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        using var document = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("error", root.GetProperty("level").GetString());
        Assert.Equal("ValueLists.ItemNotFound", root.GetProperty("messageKey").GetString());
        Assert.Equal(missingId, root.GetProperty("messageArgs").GetProperty("id").GetString());
        Assert.Equal(RenderDictionaryText("ItemNotFound", ("id", missingId)), root.GetProperty("fallbackMessage").GetString());
        Assert.False(root.TryGetProperty("message", out _));
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
            builder.UseSetting("Platform:SkipLocalAuthSeeding", "true");
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
