using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Sdk;
using ICP.Platform.Templates.BackendModuleTemplate.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Templates.BackendModuleTemplate;

public class TemplateModule : ModuleBase, IPermissionProvider, IConfigProvider
{
    public override string ModuleId => "TemplateModule";
    public override string DisplayName => "模板模块";
    public override string Description => "后端模块模板骨架";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ITemplateService, TemplateService>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "TemplateModule.View",
                DisplayName = "查看模板模块",
                ModuleId = ModuleId,
                Group = "模板模块"
            }
        };
    }

    public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
    {
        return new[]
        {
            new ConfigDescriptor
            {
                Key = "TemplateModule.Enabled",
                DisplayName = "启用模板模块",
                Type = "Bool",
                DefaultValue = "true",
                ModuleId = ModuleId,
                Group = "基础配置"
            }
        };
    }
}
