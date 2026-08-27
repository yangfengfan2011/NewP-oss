using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Example.Crud;
using ICP.Platform.Sdk;
using ICP.Platform.Modules.Example.Services;
using ICP.Platform.Crud.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Example;

public sealed class ExampleModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "example.backend";

    // Example 只注册模块自有的演示服务和资源描述；CRUD 执行流程仍由平台内核收口。
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddStandardCrudKernel();
        services.AddStandardCrudResource(ExampleRecordCrudDescriptorFactory.Create());
        services.AddScoped<IStandardCrudDisplayValueResolver, ExampleRecordDisplayValueResolver>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IRuntimeWorkloadService, RuntimeWorkloadService>();
    }
}
