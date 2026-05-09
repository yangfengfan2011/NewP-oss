using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Modules.ConfigCenter.Services;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigServiceTests
{
    private readonly IConfigService _configService;
    private readonly IConfigRepository _configRepository;
    private int _nextConfigItemId;

    public ConfigServiceTests()
    {
        _configRepository = new ConfigRepository();
        _configService = new ConfigService(_configRepository, new PlatformObjectIdGenerator());
    }

    [Fact]
    public void IConfigService_PublicContract_DoesNotExposeSchemaDefinitionSync()
    {
        // ConfigCenter 只承载 ConfigItem 值层，不应该把 SettingDefinition/ConfigDescriptor 同步入口暴露给调用方。
        var methodNames = typeof(IConfigService)
            .GetMethods()
            .Select(method => method.Name)
            .ToList();

        Assert.DoesNotContain("SyncConfigSchemaAsync", methodNames);
    }

    [Fact]
    public async Task GetConfig_GlobalConfig_ReturnsConfig()
    {
        // Arrange
        var item = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "value1",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1"
        };
        await _configRepository.CreateAsync(item);

        // Act
        var result = await _configService.GetConfigAsync("Test.Setting1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test.Setting1", result.Key);
        Assert.Equal("value1", result.Value);
    }

    [Fact]
    public async Task GetConfig_EnvironmentSpecific_ReturnsEnvironmentConfig()
    {
        // Arrange
        var globalItem = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "global",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1"
        };
        var envItem = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "production",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1",
            Environment = "Production"
        };
        await _configRepository.CreateAsync(globalItem);
        await _configRepository.CreateAsync(envItem);

        // Act
        var result = await _configService.GetConfigAsync("Test.Setting1", environment: "Production");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("production", result.Value);
    }

    [Fact]
    public async Task GetConfig_TenantSpecific_ReturnsTenantConfig()
    {
        // Arrange
        var globalItem = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "global",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1"
        };
        var tenantItem = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "tenant1",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1",
            TenantId = "tenant1"
        };
        await _configRepository.CreateAsync(globalItem);
        await _configRepository.CreateAsync(tenantItem);

        // Act
        var result = await _configService.GetConfigAsync("Test.Setting1", tenantId: "tenant1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("tenant1", result.Value);
    }

    [Fact]
    public async Task GetConfig_EnvironmentAndTenant_ReturnsHighestPriorityConfig()
    {
        // Arrange
        var globalItem = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "global",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1"
        };
        var envItem = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "production",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1",
            Environment = "Production"
        };
        var tenantEnvItem = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "tenant1-production",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1",
            Environment = "Production",
            TenantId = "tenant1"
        };
        await _configRepository.CreateAsync(globalItem);
        await _configRepository.CreateAsync(envItem);
        await _configRepository.CreateAsync(tenantEnvItem);

        // Act
        var result = await _configService.GetConfigAsync("Test.Setting1", environment: "Production", tenantId: "tenant1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("tenant1-production", result.Value);
    }

    [Fact]
    public async Task GetConfigsByModule_ReturnsModuleConfigs()
    {
        // Arrange
        var item1 = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "value1",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1"
        };
        var item2 = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting2",
            Value = "value2",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置2"
        };
        var item3 = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Other.Setting1",
            Value = "value3",
            Type = "String",
            ModuleId = "Other",
            DisplayName = "其他设置1"
        };
        await _configRepository.CreateAsync(item1);
        await _configRepository.CreateAsync(item2);
        await _configRepository.CreateAsync(item3);

        // Act
        var result = await _configService.GetConfigsByModuleAsync("Test");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Equal("Test", item.ModuleId));
    }

    [Fact]
    public async Task CreateConfig_CreatesNewConfig()
    {
        // Arrange
        var dto = new ConfigItemDto
        {
            Key = "Test.NewSetting",
            Value = "newvalue",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "新设置"
        };

        // Act
        var result = await _configService.CreateConfigAsync(dto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test.NewSetting", result.Key);
        Assert.Equal("newvalue", result.Value);

        var retrieved = await _configService.GetConfigAsync("Test.NewSetting");
        Assert.NotNull(retrieved);
    }

    [Fact]
    public async Task UpdateConfig_UpdatesExistingConfig()
    {
        // Arrange
        var item = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "oldvalue",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "测试设置1"
        };
        await _configRepository.CreateAsync(item);

        var updateDto = new ConfigItemDto
        {
            Value = "newvalue"
        };

        // Act
        var result = await _configService.UpdateConfigAsync(item.Id, updateDto);

        // Assert
        Assert.Equal("newvalue", result.Value);

        var retrieved = await _configService.GetConfigAsync("Test.Setting1");
        Assert.NotNull(retrieved);
        Assert.Equal("newvalue", retrieved.Value);
    }

    [Fact]
    public async Task GetConfig_SensitiveConfig_MasksValue()
    {
        // Arrange
        var item = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Password",
            Value = "secret123",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "密码",
            IsSensitive = true
        };
        await _configRepository.CreateAsync(item);

        // Act
        var result = await _configService.GetConfigAsync("Test.Password");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("******", result.Value);
    }

    [Fact]
    public async Task GetConfigsGrouped_GroupsByModuleAndGroup()
    {
        // Arrange
        var item1 = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting1",
            Value = "value1",
            Type = "String",
            ModuleId = "Test",
            Group = "Group1",
            DisplayName = "测试设置1"
        };
        var item2 = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting2",
            Value = "value2",
            Type = "String",
            ModuleId = "Test",
            Group = "Group1",
            DisplayName = "测试设置2"
        };
        var item3 = new ConfigItem
        {
            Id = NextConfigItemId(),
            Key = "Test.Setting3",
            Value = "value3",
            Type = "String",
            ModuleId = "Test",
            Group = "Group2",
            DisplayName = "测试设置3"
        };
        await _configRepository.CreateAsync(item1);
        await _configRepository.CreateAsync(item2);
        await _configRepository.CreateAsync(item3);

        // Act
        var result = await _configService.GetConfigsGroupedAsync();

        // Assert
        Assert.Equal(2, result.Count);
        var group1 = result.FirstOrDefault(g => g.Group == "Group1");
        Assert.NotNull(group1);
        Assert.Equal(2, group1.Items.Count);
    }

    private string NextConfigItemId()
    {
        _nextConfigItemId++;
        return $"ConfigItem_{_nextConfigItemId:00000000000000000000000000000000}";
    }
}
