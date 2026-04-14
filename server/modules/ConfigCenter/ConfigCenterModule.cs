using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.ConfigCenter.Imports;
using ICP.Platform.Modules.ConfigCenter.Persistence.Ef;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Modules.ConfigCenter.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.ConfigCenter;

public class ConfigCenterModule : ModuleBase, IConfigProvider, IMenuProvider, IPermissionProvider
{
    public override string ModuleId => "ConfigCenter";
    public override string DisplayName => "配置中心";
    public override string Version => "1.0.0";
    public override string Description => "平台统一配置管理模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IConfigService, ConfigService>();
        services.AddScoped<IConfigRepository, EfConfigRepository>();
        services.AddScoped<IValueListService, ValueListService>();
        services.AddScoped<IValueListRepository, EfValueListRepository>();
        services.AddScoped<ILegacySettingParser, LegacySettingXmlParser>();
        services.AddScoped<ILegacySettingImportService, LegacySettingImportService>();
    }

    // 这里声明的是 ConfigItem 的模块默认描述符，用于初始化和运行时消费。
    // 定义层 authoring 编辑能力归 MetadataConfigCenter 管，不在 ConfigCenter 内部继续扩散。
    public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
    {
        return new[]
        {
            new ConfigDescriptor
            {
                Key = "ConfigCenter.CacheEnabled",
                DisplayName = "启用配置缓存",
                Description = "是否启用配置项的内存缓存",
                Type = "Bool",
                DefaultValue = "true",
                Required = false,
                ModuleId = ModuleId,
                Group = "性能"
            },
            new ConfigDescriptor
            {
                Key = "ConfigCenter.CacheDuration",
                DisplayName = "缓存时长（秒）",
                Description = "配置项在内存中的缓存时长",
                Type = "Int",
                DefaultValue = "300",
                Required = false,
                ModuleId = ModuleId,
                Group = "性能"
            },
            new ConfigDescriptor
            {
                Key = "ConfigCenter.AuditEnabled",
                DisplayName = "启用配置审计",
                Description = "是否记录配置变更的审计日志",
                Type = "Bool",
                DefaultValue = "true",
                Required = false,
                ModuleId = ModuleId,
                Group = "安全"
            }
        };
    }

    public IReadOnlyList<MenuDescriptor> GetMenus()
    {
        return new[]
        {
            new MenuDescriptor
            {
                Id = "ConfigCenter",
                DisplayName = "配置中心",
                ParentId = null,
                Route = "/config",
                Icon = "settings",
                Order = 20,
                RequiredPermission = "ConfigCenter.Config.View",
                ModuleId = ModuleId
            },
            new MenuDescriptor
            {
                Id = "ConfigCenter.Configs",
                DisplayName = "配置管理",
                ParentId = "ConfigCenter",
                Route = "/config/items",
                Icon = "cog",
                Order = 1,
                RequiredPermission = "ConfigCenter.Config.View",
                ModuleId = ModuleId
            },
            new MenuDescriptor
            {
                Id = "ConfigCenter.ValueLists",
                DisplayName = "值列表管理",
                ParentId = "ConfigCenter",
                Route = "/config/value-lists",
                Icon = "list",
                Order = 2,
                RequiredPermission = "ConfigCenter.Config.View",
                ModuleId = ModuleId
            }
        };
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "ConfigCenter.Config.View",
                DisplayName = "查看配置",
                Description = "允许查看配置项列表和详情",
                ModuleId = ModuleId,
                Group = "配置管理"
            },
            new PermissionDescriptor
            {
                Code = "ConfigCenter.Config.Create",
                DisplayName = "创建配置",
                Description = "允许创建新配置项",
                ModuleId = ModuleId,
                Group = "配置管理"
            },
            new PermissionDescriptor
            {
                Code = "ConfigCenter.Config.Edit",
                DisplayName = "编辑配置",
                Description = "允许编辑配置项",
                ModuleId = ModuleId,
                Group = "配置管理"
            },
            new PermissionDescriptor
            {
                Code = "ConfigCenter.Config.Delete",
                DisplayName = "删除配置",
                Description = "允许删除配置项",
                ModuleId = ModuleId,
                Group = "配置管理"
            }
        };
    }
}
