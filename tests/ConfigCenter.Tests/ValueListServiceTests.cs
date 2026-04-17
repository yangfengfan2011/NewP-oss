using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Modules.ConfigCenter.Services;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ValueListServiceTests
{
    private readonly IValueListService _service;
    private readonly IValueListRepository _repository;

    public ValueListServiceTests()
    {
        _repository = new ValueListRepository();
        _service = new ValueListService(_repository);
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
    public async Task CreateAsync_DuplicateCode_ThrowsInvalidOperationException()
    {
        await _service.CreateAsync(new ValueListItemDto
        {
            Code = "STATUS_ACTIVE",
            Name = "启用"
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(
            new ValueListItemDto
            {
                Code = "STATUS_ACTIVE",
                Name = "重复"
            }));

        Assert.Contains("已存在", error.Message);
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
}
