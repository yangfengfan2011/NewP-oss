using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Demo;

public class DemoModule : ModuleBase, IPermissionProvider, IMenuProvider, IConfigProvider
{
    public override string ModuleId => "Demo";
    public override string DisplayName => "演示模块";
    public override string Version => "1.0.0";
    public override string Description => "用于演示模块契约和接入机制的示例模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册演示服务
        services.AddScoped<IDemoService, DemoService>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "Demo.View",
                DisplayName = "查看演示",
                Description = "允许查看演示模块内容",
                ModuleId = ModuleId,
                Group = "演示"
            },
            new PermissionDescriptor
            {
                Code = "Demo.Manage",
                DisplayName = "管理演示",
                Description = "允许管理演示模块",
                ModuleId = ModuleId,
                Group = "演示"
            }
        };
    }

    public IReadOnlyList<MenuDescriptor> GetMenus()
    {
        return new[]
        {
            new MenuDescriptor
            {
                Id = "Demo",
                DisplayName = "演示模块",
                ParentId = null,
                Route = "/demo",
                Icon = "demo-icon",
                Order = 100,
                RequiredPermission = "Demo.View",
                ModuleId = ModuleId
            },
            new MenuDescriptor
            {
                Id = "Demo.Dashboard",
                DisplayName = "演示仪表板",
                ParentId = "Demo",
                Route = "/demo/dashboard",
                Icon = "dashboard-icon",
                Order = 1,
                RequiredPermission = "Demo.View",
                ModuleId = ModuleId
            }
        };
    }

    public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
    {
        return new[]
        {
            new ConfigDescriptor
            {
                Key = "Demo.EnableFeatureX",
                DisplayName = "启用功能 X",
                Description = "是否启用演示功能 X",
                Type = "Bool",
                DefaultValue = "true",
                Required = false,
                ModuleId = ModuleId,
                Group = "功能开关"
            },
            new ConfigDescriptor
            {
                Key = "Demo.MaxItems",
                DisplayName = "最大项目数",
                Description = "演示模块最多显示的项目数量",
                Type = "Int",
                DefaultValue = "100",
                Required = true,
                ModuleId = ModuleId,
                Group = "限制"
            }
        };
    }
}

public interface IDemoService
{
    string GetMessage();
}

public class DemoService : IDemoService
{
    public string GetMessage() => "Hello from Demo Module!";
}
