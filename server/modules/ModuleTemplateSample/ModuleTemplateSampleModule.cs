using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.ModuleTemplateSample.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.ModuleTemplateSample;

public class ModuleTemplateSampleModule : ModuleBase, IPermissionProvider, IConfigProvider
{
    public override string ModuleId => "ModuleTemplateSample";
    public override string DisplayName => "模块模板示例";
    public override string Description => "用于验证后端组件化模板的真实示例模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISampleStatusService, SampleStatusService>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "ModuleTemplateSample.View",
                DisplayName = "查看模块模板示例",
                ModuleId = ModuleId,
                Group = "模块模板示例"
            }
        };
    }

    public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
    {
        return new[]
        {
            new ConfigDescriptor
            {
                Key = "ModuleTemplateSample.Enabled",
                DisplayName = "启用模块模板示例",
                Type = "Bool",
                DefaultValue = "true",
                ModuleId = ModuleId,
                Group = "基础配置"
            }
        };
    }
}
