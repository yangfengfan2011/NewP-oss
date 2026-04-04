using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class ModuleSystemTests
{
    [Fact]
    public void ModuleRegistry_CanRegisterModule()
    {
        // Arrange
        var registry = new ModuleRegistry();
        var module = new TestModule();

        // Act
        registry.RegisterModule(module);

        // Assert
        var modules = registry.GetAllModules();
        Assert.Single(modules);
        Assert.Equal("Test", modules["Test"].ModuleId);
    }

    [Fact]
    public void ModuleRegistry_CollectsPermissions()
    {
        // Arrange
        var registry = new ModuleRegistry();
        var module = new TestModuleWithPermissions();

        // Act
        registry.RegisterModule(module);

        // Assert
        var permissions = registry.GetAllPermissions();
        Assert.Equal(2, permissions.Count);
        Assert.Contains(permissions, p => p.Code == "Test.View");
        Assert.Contains(permissions, p => p.Code == "Test.Manage");
    }

    [Fact]
    public void ModuleRegistry_CollectsMenus()
    {
        // Arrange
        var registry = new ModuleRegistry();
        var module = new TestModuleWithMenus();

        // Act
        registry.RegisterModule(module);

        // Assert
        var menus = registry.GetAllMenus();
        Assert.Single(menus);
        Assert.Equal("Test.Menu", menus[0].Id);
    }

    [Fact]
    public void ModuleRegistry_CollectsConfigs()
    {
        // Arrange
        var registry = new ModuleRegistry();
        var module = new TestModuleWithConfigs();

        // Act
        registry.RegisterModule(module);

        // Assert
        var configs = registry.GetAllConfigs();
        Assert.Single(configs);
        Assert.Equal("Test.Config", configs[0].Key);
    }

    [Fact]
    public void ModuleRegistry_CollectsMigrations()
    {
        // Arrange
        var registry = new ModuleRegistry();
        var module = new TestModuleWithMigrations();

        // Act
        registry.RegisterModule(module);

        // Assert
        var migrations = registry.GetAllMigrations();
        Assert.Single(migrations);
        Assert.Equal("Test.Migration", migrations[0].Id);
    }

    [Fact]
    public void ModuleRegistry_DuplicateModuleId_ThrowsException()
    {
        // Arrange
        var registry = new ModuleRegistry();
        var module = new TestModule();
        registry.RegisterModule(module);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => registry.RegisterModule(module));
    }

    [Fact]
    public void AssemblyModuleLoader_DiscoversModules()
    {
        // Arrange
        var loader = new AssemblyModuleLoader(new[] { typeof(TestModule).Assembly });

        // Act
        var modules = loader.DiscoverModules();

        // Assert
        Assert.NotEmpty(modules);
    }

    [Fact]
    public void AssemblyModuleLoader_SortsByDependencies()
    {
        // Arrange
        var loader = new AssemblyModuleLoader(new[] { typeof(TestModule).Assembly });
        var moduleA = new TestModule();
        moduleA.SetModuleId("A");
        var moduleB = new TestModuleWithDependency();
        moduleB.SetModuleId("B");
        moduleB.SetDependsOn("A");
        var modules = new List<IModule> { moduleB, moduleA };

        // Act
        var sorted = loader.SortByDependencies(modules);

        // Assert
        Assert.Equal(2, sorted.Count);
        Assert.Equal("A", sorted[0].ModuleId);
        Assert.Equal("B", sorted[1].ModuleId);
    }
}

// 测试用模块
public class TestModule : ModuleBase
{
    private string _moduleId = "Test";
    public override string ModuleId => _moduleId;
    public override string DisplayName => "测试模块";

    public void SetModuleId(string id) => _moduleId = id;
}

public class TestModuleWithPermissions : ModuleBase, IPermissionProvider
{
    public override string ModuleId => "TestPermissions";
    public override string DisplayName => "测试权限模块";

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "Test.View",
                DisplayName = "查看",
                ModuleId = ModuleId
            },
            new PermissionDescriptor
            {
                Code = "Test.Manage",
                DisplayName = "管理",
                ModuleId = ModuleId
            }
        };
    }
}

public class TestModuleWithMenus : ModuleBase, IMenuProvider
{
    public override string ModuleId => "TestMenus";
    public override string DisplayName => "测试菜单模块";

    public IReadOnlyList<MenuDescriptor> GetMenus()
    {
        return new[]
        {
            new MenuDescriptor
            {
                Id = "Test.Menu",
                DisplayName = "测试菜单",
                ModuleId = ModuleId
            }
        };
    }
}

public class TestModuleWithConfigs : ModuleBase, IConfigProvider
{
    public override string ModuleId => "TestConfigs";
    public override string DisplayName => "测试配置模块";

    public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
    {
        return new[]
        {
            new ConfigDescriptor
            {
                Key = "Test.Config",
                DisplayName = "测试配置",
                Type = "String",
                ModuleId = ModuleId
            }
        };
    }
}

public class TestModuleWithMigrations : ModuleBase, IMigrationProvider
{
    public override string ModuleId => "TestMigrations";
    public override string DisplayName => "测试迁移模块";

    public IReadOnlyList<MigrationDescriptor> GetMigrations()
    {
        return new[]
        {
            new MigrationDescriptor
            {
                Id = "Test.Migration",
                DisplayName = "测试迁移",
                SourceType = "OldP",
                ModuleId = ModuleId,
                HandlerType = "TestHandler"
            }
        };
    }
}

public class TestModuleWithDependency : ModuleBase
{
    private string _moduleId = "TestDep";
    private string _dependsOn = string.Empty;

    public override string ModuleId => _moduleId;
    public override string DisplayName => "测试依赖模块";
    public override IReadOnlyList<string> Dependencies => new[] { _dependsOn };

    public void SetModuleId(string id) => _moduleId = id;
    public void SetDependsOn(string id) => _dependsOn = id;
}
