using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Sdk;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Modules.Identity;

/// <summary>
/// Identity 必需内建模块的最小行为绑定。
/// </summary>
public sealed class IdentityModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "identity.backend";

    public override void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPlatformObjectIdGeneration();
        services.AddOptions<IdentityCredentialOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                if (bool.TryParse(
                    configuration["Identity:Credentials:UseSharedDevelopmentCredential"],
                    out var useSharedDevelopmentCredential))
                {
                    options.UseSharedDevelopmentCredential = useSharedDevelopmentCredential;
                }
            })
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<IdentityCredentialOptions>>(serviceProvider =>
            new IdentityCredentialOptionsValidator(
                serviceProvider.GetRequiredService<IWebHostEnvironment>().EnvironmentName));
        services.AddScoped<IIdentityRepository, EfIdentityRepository>();
        services.AddScoped<IIdentityAccountDirectory, IdentityAccountDirectory>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<ITemporaryCredentialIssuer>(serviceProvider =>
        {
            var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
            var options = serviceProvider.GetRequiredService<IOptions<IdentityCredentialOptions>>().Value;
            return new DefaultTemporaryCredentialIssuer(
                environment.EnvironmentName,
                options.UseSharedDevelopmentCredential);
        });
        // Authentication 的注册表由后续模块拥有；工厂把跨模块依赖保持在组合边界，
        // 也允许当前 Identity 内核在注册表尚未装配时继续完成静态构建与模型验证。
        services.AddScoped<IIdentityAccountApplicationService>(serviceProvider =>
            new IdentityAccountApplicationService(
                serviceProvider.GetRequiredService<IIdentityRepository>(),
                serviceProvider.GetRequiredService<IPlatformTransactionExecutor>(),
                serviceProvider.GetRequiredService<IAuthenticationSessionRegistry>(),
                serviceProvider.GetRequiredService<IPasswordHasher<User>>(),
                serviceProvider.GetRequiredService<ITemporaryCredentialIssuer>(),
                serviceProvider.GetRequiredService<IPlatformObjectIdGenerator>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IIdentityLifecycleParticipant>(serviceProvider =>
            new IdentityLifecycleParticipant(
                serviceProvider.GetRequiredService<IIdentityRepository>(),
                serviceProvider.GetRequiredService<IAuthenticationSessionRegistry>(),
                serviceProvider.GetRequiredService<IPlatformObjectIdGenerator>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IIdentityLocalAdministratorMaintenanceService>(serviceProvider =>
            new IdentityLocalAdministratorMaintenanceService(
                serviceProvider.GetRequiredService<IIdentityRepository>(),
                serviceProvider.GetRequiredService<IPlatformTransactionExecutor>(),
                serviceProvider.GetRequiredService<IAuthenticationSessionRegistry>(),
                serviceProvider.GetRequiredService<ILocalAdministratorRoleAssignment>(),
                serviceProvider.GetRequiredService<IPasswordHasher<User>>(),
                serviceProvider.GetRequiredService<ITemporaryCredentialIssuer>(),
                serviceProvider.GetRequiredService<IPlatformObjectIdGenerator>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IIdentityLocalAdministratorReadinessProbe, IdentityLocalAdministratorReadinessProbe>();
    }
}
