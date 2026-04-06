using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.ModuleTemplateSample.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.ModuleTemplateSample.Tests;

public class ModuleTemplateSampleModuleTests
{
    private readonly ModuleTemplateSampleModule _module;

    public ModuleTemplateSampleModuleTests()
    {
        _module = new ModuleTemplateSampleModule();
    }

    [Fact]
    public void ModuleId_ReturnsCorrectValue()
    {
        // 执行 & 断言
        Assert.Equal("ModuleTemplateSample", _module.ModuleId);
    }

    [Fact]
    public void DisplayName_ReturnsCorrectValue()
    {
        // 执行 & 断言
        Assert.Equal("模块模板示例", _module.DisplayName);
    }

    [Fact]
    public void Description_ReturnsCorrectValue()
    {
        // 执行 & 断言
        Assert.Equal("用于验证后端组件化模板的真实示例模块", _module.Description);
    }

    [Fact]
    public void ConfigureServices_RegistersSampleStatusService()
    {
        // 准备
        var services = new ServiceCollection();

        // 执行
        _module.ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // 断言
        var service = serviceProvider.GetService<ISampleStatusService>();
        Assert.NotNull(service);
        Assert.IsType<SampleStatusService>(service);
    }

    [Fact]
    public void GetPermissions_ReturnsExpectedPermissions()
    {
        // 执行
        var permissions = _module.GetPermissions();

        // 断言
        Assert.Single(permissions);
        var permission = permissions[0];
        Assert.Equal("ModuleTemplateSample.View", permission.Code);
        Assert.Equal("查看模块模板示例", permission.DisplayName);
        Assert.Equal("ModuleTemplateSample", permission.ModuleId);
        Assert.Equal("模块模板示例", permission.Group);
    }

    [Fact]
    public void GetConfigSchema_ReturnsExpectedConfigs()
    {
        // 执行
        var configs = _module.GetConfigSchema();

        // 断言
        Assert.Single(configs);
        var config = configs[0];
        Assert.Equal("ModuleTemplateSample.Enabled", config.Key);
        Assert.Equal("启用模块模板示例", config.DisplayName);
        Assert.Equal("Bool", config.Type);
        Assert.Equal("true", config.DefaultValue);
        Assert.Equal("ModuleTemplateSample", config.ModuleId);
        Assert.Equal("基础配置", config.Group);
    }
}
