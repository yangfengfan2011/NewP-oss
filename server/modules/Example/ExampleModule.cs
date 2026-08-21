using ICP.Platform.Sdk;
using ICP.Platform.Modules.Example.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Example;

public sealed class ExampleModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "example.backend";

    // Example 只注册演示场景自己的服务；Host 不包含动态列或日期计算。
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IRuntimeWorkloadService, RuntimeWorkloadService>();
    }
}
