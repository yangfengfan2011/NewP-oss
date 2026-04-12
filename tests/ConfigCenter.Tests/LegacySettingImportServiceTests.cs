using ICP.Platform.Modules.ConfigCenter.Imports;
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using Xunit;

namespace ConfigCenter.Tests;

public class LegacySettingImportServiceTests
{
    private class FakeParser : ILegacySettingParser
    {
        private readonly List<LegacySettingImportItem> _items;

        public FakeParser(List<LegacySettingImportItem> items)
        {
            _items = items;
        }

        public IEnumerable<LegacySettingImportItem> Parse(string xmlContent, string sourceFileName)
        {
            return _items;
        }
    }

    private class FakeRepository : IConfigRepository
    {
        private readonly Dictionary<string, ConfigItem> _items = new();
        public List<ConfigItem> CreatedItems { get; } = new();
        public List<ConfigItem> UpdatedItems { get; } = new();

        public Task<List<ConfigItem>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.Values.ToList());
        }

        public Task<ConfigItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.Values.FirstOrDefault(x => x.Id == id));
        }

        public Task<ConfigItem?> GetByKeyAsync(string key, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.GetValueOrDefault(key));
        }

        public Task<List<ConfigItem>> GetByModuleAsync(string moduleId, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.Values.Where(x => x.ModuleId == moduleId).ToList());
        }

        public Task<List<ConfigItem>> GetByKeysAsync(IEnumerable<string> keys, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.Where(x => keys.Contains(x.Key)).Select(x => x.Value).ToList());
        }

        public Task CreateAsync(ConfigItem item, CancellationToken cancellationToken = default)
        {
            _items[item.Key] = item;
            CreatedItems.Add(item);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ConfigItem item, CancellationToken cancellationToken = default)
        {
            _items[item.Key] = item;
            UpdatedItems.Add(item);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var item = _items.Values.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                _items.Remove(item.Key);
            }
            return Task.CompletedTask;
        }

        public void AddExisting(ConfigItem item)
        {
            _items[item.Key] = item;
        }
    }

    [Fact]
    public async Task ImportAsync_CreatesNewConfigs_WhenNotExist()
    {
        // Arrange
        var xmlContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ArrayOfAppSet>
  <AppSet Key=""TestKey1"" Value=""true"" ToClient=""true"" Description=""Test bool config"" />
  <AppSet Key=""TestKey2"" Value=""123"" ToClient=""false"" Description=""Test int config"" />
</ArrayOfAppSet>";

        var request = new LegacySettingImportRequest
        {
            XmlContent = xmlContent,
            SourceFileName = "test.setting",
            ModuleId = "TestModule",
            Environment = null,
            TenantId = null
        };

        var parsedItems = new List<LegacySettingImportItem>
        {
            new() { Key = "TestKey1", Value = "true", ToClient = true, Description = "Test bool config", Group = "test", SourceFile = "test.setting" },
            new() { Key = "TestKey2", Value = "123", ToClient = false, Description = "Test int config", Group = "test", SourceFile = "test.setting" }
        };

        var parser = new FakeParser(parsedItems);
        var repository = new FakeRepository();
        var service = new LegacySettingImportService(parser, repository);

        // Act
        var result = await service.ImportAsync(request, "testUser");

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Empty(result.Errors);

        Assert.Equal(2, repository.CreatedItems.Count);

        var item1 = repository.CreatedItems.First(c => c.Key == "TestKey1");
        Assert.Equal("true", item1.Value);
        Assert.Equal("Bool", item1.Type);
        Assert.Equal("TestModule", item1.ModuleId);
        Assert.Equal("test", item1.Group);
        Assert.Equal("TestKey1", item1.DisplayName);
        Assert.Equal("Test bool config", item1.Description);

        var item2 = repository.CreatedItems.First(c => c.Key == "TestKey2");
        Assert.Equal("123", item2.Value);
        Assert.Equal("Int", item2.Type);
        Assert.Equal("TestModule", item2.ModuleId);
    }

    [Fact]
    public async Task ImportAsync_UpdatesExistingConfigs_OnlyMutatesValueLayerFields()
    {
        // Arrange
        var xmlContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ArrayOfAppSet>
  <AppSet Key=""ExistingKey"" Value=""newValue"" ToClient=""true"" Description=""Updated description"" />
</ArrayOfAppSet>";

        var request = new LegacySettingImportRequest
        {
            XmlContent = xmlContent,
            SourceFileName = "update.setting",
            ModuleId = "UpdatedModule",
            Environment = null,
            TenantId = null
        };

        var parsedItems = new List<LegacySettingImportItem>
        {
            new() { Key = "ExistingKey", Value = "newValue", ToClient = true, Description = "Updated description", Group = "update", SourceFile = "update.setting" }
        };

        var existingConfig = new ConfigItem
        {
            Id = "existing-id",
            Key = "ExistingKey",
            Value = "oldValue",
            Type = "String",
            ModuleId = "OldModule",
            Group = "old",
            DisplayName = "Old Display",
            Description = "Old description",
            IsSensitive = false
        };

        var parser = new FakeParser(parsedItems);
        var repository = new FakeRepository();
        repository.AddExisting(existingConfig);
        var service = new LegacySettingImportService(parser, repository);

        // Act
        var result = await service.ImportAsync(request, "testUser");

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Empty(result.Errors);

        Assert.Single(repository.UpdatedItems);
        var updated = repository.UpdatedItems[0];
        Assert.Equal("existing-id", updated.Id);
        Assert.Equal("ExistingKey", updated.Key);
        Assert.Equal("newValue", updated.Value);
        Assert.Equal("String", updated.Type);
        Assert.Equal("OldModule", updated.ModuleId);
        Assert.Equal("old", updated.Group);
        Assert.Equal("Old Display", updated.DisplayName);
        Assert.Equal("Old description", updated.Description);
        Assert.Equal("testUser", updated.UpdatedBy);
    }
}
