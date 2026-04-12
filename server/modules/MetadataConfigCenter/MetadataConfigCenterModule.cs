using ICP.Platform.Modules.MetadataConfigCenter.Adapters;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.MetadataConfigCenter.Services;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.MetadataConfigCenter;

public class MetadataConfigCenterModule : ModuleBase, IMenuProvider, IPermissionProvider
{
    public override string ModuleId => "MetadataConfigCenter";
    public override string DisplayName => "元数据配置中心";
    public override string Version => "1.0.0";
    public override string Description => "模块元数据定义层的独立可视化编辑入口";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IMetadataManifestService, MetadataManifestService>();
        services.AddScoped<ModuleMetadataStore>();
        services.AddScoped<SettingDefinitionAuthoringAdapter>();
        services.AddScoped<ISettingDefinitionAuthoringService, SettingDefinitionAuthoringService>();
    }

    public IReadOnlyList<MenuDescriptor> GetMenus()
    {
        return
        [
            new MenuDescriptor
            {
                Id = "MetadataConfigCenter",
                DisplayName = "元数据配置中心",
                ParentId = null,
                Route = null,
                Icon = "database",
                Order = 21,
                RequiredPermission = "MetadataConfigCenter.View",
                ModuleId = ModuleId
            },
            new MenuDescriptor
            {
                Id = "MetadataConfigCenter.Modules",
                DisplayName = "模块概览",
                ParentId = "MetadataConfigCenter",
                Route = "/metadata-config/modules",
                Icon = "grid",
                Order = 1,
                RequiredPermission = "MetadataConfigCenter.View",
                ModuleId = ModuleId
            }
        ];
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return
        [
            new PermissionDescriptor
            {
                Code = "MetadataConfigCenter.View",
                DisplayName = "查看元数据配置中心",
                Description = "允许查看模块元数据配置中心页面",
                ModuleId = ModuleId,
                Group = "元数据配置中心"
            },
            new PermissionDescriptor
            {
                Code = "MetadataConfigCenter.Edit",
                DisplayName = "编辑元数据定义",
                Description = "允许编辑模块元数据定义层内容",
                ModuleId = ModuleId,
                Group = "元数据配置中心"
            },
            new PermissionDescriptor
            {
                Code = "MetadataConfigCenter.Apply",
                DisplayName = "应用元数据变更",
                Description = "允许执行元数据应用动作",
                ModuleId = ModuleId,
                Group = "元数据配置中心"
            },
            new PermissionDescriptor
            {
                Code = "MetadataConfigCenter.GitSync",
                DisplayName = "同步元数据 Git 变更",
                Description = "允许执行元数据 Git 同步动作",
                ModuleId = ModuleId,
                Group = "元数据配置中心"
            }
        ];
    }
}
