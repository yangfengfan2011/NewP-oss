using ICP.Platform.Modules.MetadataConfigCenter.Adapters;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.MetadataConfigCenter.Services;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.MetadataConfigCenter;

public class MetadataConfigCenterModule : ModuleBase, IMenuProvider
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
        services.AddScoped<ISettingDefinitionApplyService, SettingDefinitionApplyService>();
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
                ModuleId = ModuleId
            }
        ];
    }

}
