using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authentication.Application;
using ICP.Platform.Modules.Authentication.Persistence.Ef;
using ICP.Platform.Modules.Authentication.Repositories;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Modules.Authentication;

/// <summary>
/// Authentication 必需内建模块的最小行为绑定。
/// </summary>
public sealed class AuthenticationModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "authentication.backend";

    // 注册 Authentication 会话能力，并要求部署配置在 Host Ready 前通过验证。
    public override void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPlatformObjectIdGeneration();
        services.AddOptions<AuthenticationSessionOptions>()
            .BindConfiguration(AuthenticationSessionOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AuthenticationSessionOptions>, AuthenticationSessionOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAuthenticationSessionCredentialGenerator, DefaultAuthenticationSessionCredentialGenerator>();
        services.AddScoped<IAuthenticationSessionRepository, EfAuthenticationSessionRepository>();
        services.AddScoped<IAuthenticationSessionRegistry>(serviceProvider =>
            new AuthenticationSessionRegistry(
                serviceProvider.GetRequiredService<IAuthenticationSessionRepository>(),
                serviceProvider.GetRequiredService<IIdentityAccountDirectory>(),
                serviceProvider.GetRequiredService<IPlatformObjectIdGenerator>(),
                serviceProvider.GetRequiredService<IAuthenticationSessionCredentialGenerator>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<IOptions<AuthenticationSessionOptions>>().Value));
        services.AddScoped<IAuthenticationSessionPurgeService>(serviceProvider =>
            new AuthenticationSessionPurgeService(
                serviceProvider.GetRequiredService<IAuthenticationSessionRepository>(),
                serviceProvider.GetRequiredService<IPlatformTransactionExecutor>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<IOptions<AuthenticationSessionOptions>>().Value));
    }
}
