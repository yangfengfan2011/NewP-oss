using ICP.Platform.Sdk;
using ICP.Platform.Templates.BackendModuleTemplate.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Templates.BackendModuleTemplate;

public sealed class TemplateModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "template-module.backend";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ITemplateService, TemplateService>();
    }
}
