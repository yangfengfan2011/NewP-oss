using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.ExternalPluginTemplate.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.ExternalPluginTemplate;

public class ExternalPluginModule : ModuleBase
{
    public override string ModuleId => "ExternalPlugin";
    public override string DisplayName => "外部插件模板";
    public override string Description => "面向仓库外部开发的最小后端插件模板";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IExternalPluginStatusService, ExternalPluginStatusService>();
    }

}
