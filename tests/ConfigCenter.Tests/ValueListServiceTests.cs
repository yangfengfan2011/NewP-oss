using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Modules.ConfigCenter.Services;
using ICP.Platform.Sdk.UserMessages;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ValueListServiceTests
{
    private readonly IValueListService _service;
    private readonly IValueListRepository _repository;

    public ValueListServiceTests()
    {
        _repository = new ValueListRepository();
        _service = new ValueListService(_repository, new PlatformObjectIdGenerator());
    }

    [Fact]
    public async Task CreateAsync_CreatesNewItem()
    {
        var result = await _service.CreateAsync(new ValueListItemDto
        {
            Code = "STATUS_ACTIVE",
            Name = "启用",
            Description = "启用状态"
        });

        Assert.Equal("STATUS_ACTIVE", result.Code);
        Assert.Equal("启用", result.Name);

        var items = await _service.GetAllAsync();
        Assert.Single(items);
    }

    [Fact]
    public async Task CreateAsync_AssignsValueListItemPrefixedId()
    {
        var service = new ValueListService(
            new ValueListRepository(),
            new FixedObjectIdGenerator("ValueListItem_00000000000000000000000000000001"));

        var created = await service.CreateAsync(new ValueListItemDto
        {
            Code = "protocol",
            DisplayName = "协议"
        });

        Assert.Equal("ValueListItem_00000000000000000000000000000001", created.Id);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsUserMessageException()
    {
        await _service.CreateAsync(new ValueListItemDto
        {
            Code = "STATUS_ACTIVE",
            Name = "启用"
        });

        var error = await Assert.ThrowsAsync<UserMessageException>(() => _service.CreateAsync(
            new ValueListItemDto
            {
                Code = "STATUS_ACTIVE",
                Name = "重复"
            }));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("ValueLists.CodeAlreadyExists", error.MessageKey);
        Assert.Equal("STATUS_ACTIVE", error.MessageArgs?["code"]);
        Assert.Equal(RenderDictionaryText("CodeAlreadyExists", ("code", "STATUS_ACTIVE")), error.FallbackMessage);
    }

    [Fact]
    public async Task DeleteAsync_MissingId_ThrowsUserMessageException()
    {
        var error = await Assert.ThrowsAsync<UserMessageException>(() => _service.DeleteAsync("missing-id"));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("ValueLists.ItemNotFound", error.MessageKey);
        Assert.Equal("missing-id", error.MessageArgs?["id"]);
        Assert.Equal(RenderDictionaryText("ItemNotFound", ("id", "missing-id")), error.FallbackMessage);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateCode_ThrowsUserMessageException()
    {
        var first = await _service.CreateAsync(new ValueListItemDto
        {
            Code = "STATUS_ACTIVE",
            Name = "启用"
        });

        var second = await _service.CreateAsync(new ValueListItemDto
        {
            Code = "STATUS_DISABLED",
            Name = "停用"
        });

        var error = await Assert.ThrowsAsync<UserMessageException>(() => _service.UpdateAsync(second.Id, new ValueListItemDto
        {
            Code = first.Code,
            Name = "重复编码"
        }));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("ValueLists.CodeAlreadyExists", error.MessageKey);
        Assert.Equal("STATUS_ACTIVE", error.MessageArgs?["code"]);
        Assert.Equal(RenderDictionaryText("CodeAlreadyExists", ("code", "STATUS_ACTIVE")), error.FallbackMessage);
    }

    [Theory]
    [InlineData("", "STATUS_ACTIVE", "ValueLists.CodeRequired")]
    [InlineData("STATUS_ACTIVE", "", "ValueLists.NameRequired")]
    public async Task CreateAsync_InvalidInput_ThrowsUserMessageException(
        string code,
        string name,
        string expectedMessageKey)
    {
        var error = await Assert.ThrowsAsync<UserMessageException>(() => _service.CreateAsync(new ValueListItemDto
        {
            Code = code,
            Name = name
        }));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(expectedMessageKey, error.MessageKey);
        Assert.True(error.MessageArgs is null || error.MessageArgs.Count == 0);
        Assert.Equal(RenderDictionaryText(expectedMessageKey.Split('.').Last()), error.FallbackMessage);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExistingItem()
    {
        var created = await _service.CreateAsync(new ValueListItemDto
        {
            Code = "STATUS_ACTIVE",
            Name = "启用"
        });

        var updated = await _service.UpdateAsync(created.Id, new ValueListItemDto
        {
            Code = "STATUS_ENABLED",
            Name = "已启用",
            Description = "编辑后的描述"
        }, "tester");

        Assert.Equal("STATUS_ENABLED", updated.Code);
        Assert.Equal("已启用", updated.Name);
        Assert.Equal("编辑后的描述", updated.Description);
        Assert.Equal("tester", updated.UpdatedBy);
    }

    [Fact]
    public async Task DeleteAsync_RemovesExistingItem()
    {
        var created = await _service.CreateAsync(new ValueListItemDto
        {
            Code = "STATUS_ACTIVE",
            Name = "启用"
        });

        await _service.DeleteAsync(created.Id);

        var items = await _service.GetAllAsync();
        Assert.Empty(items);
    }

    private static string RenderDictionaryText(string entryKey, params (string Key, string Value)[] args)
    {
        var template = LoadDictionaryTemplates()[entryKey];
        foreach (var (key, value) in args)
        {
            template = template.Replace($"{{{key}}}", value, StringComparison.Ordinal);
        }

        return template;
    }

    private static IReadOnlyDictionary<string, string> LoadDictionaryTemplates()
    {
        var dictionaryPath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "ConfigCenter"), "metadata", "dictionaries", "ValueLists.dic.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(dictionaryPath));

        return document.RootElement
            .GetProperty("ValueLists")
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetProperty("zhs").GetString() ?? string.Empty,
                StringComparer.Ordinal);
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

    private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
    {
        private readonly string _id;

        public FixedObjectIdGenerator(string id)
        {
            _id = id;
        }

        public string NewId(string objectName) => _id;
    }
}
