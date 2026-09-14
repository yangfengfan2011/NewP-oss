using ICP.Platform.HostApi.Authorization;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Kernel.Extensions;
using ICP.Platform.Modules.Authorization;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class HostPersistenceBootstrapTests
{
    [Fact]
    public void AddPlatformPersistence_RegistersMigrationRunnerAndModuleModels()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        services.AddPlatformPersistence(configuration, TestPersistencePlan.Create());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var tableNames = dbContext.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(static tableName => tableName is not null)
            .ToList();

        Assert.NotNull(runner);
        Assert.Contains("ConfigItem", tableNames);
        Assert.Contains("Role", tableNames);
        Assert.Contains("Permission", tableNames);
        Assert.Contains("RolePermission", tableNames);
        Assert.Contains("UserRole", tableNames);
    }

    [Fact]
    public void AuthorizationCatalogInitializer_IsRegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddScoped<AuthorizationCatalogInitializer>();

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(AuthorizationCatalogInitializer));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AuthorizationCatalogInitializer_AllowsValidatedProviderBuild()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<IBusinessAuditRecorder, BusinessAuditRecorder>();
        services.AddScoped<IIdentityRepository, EfIdentityRepository>();
        services.AddScoped<IIdentityAccountDirectory, IdentityAccountDirectory>();
        services.AddPlatformPersistence(configuration, TestPersistencePlan.Create());
        services.AddModuleSystem(options =>
        {
            options.Assemblies.Add(typeof(AuthorizationModule).Assembly);
        });
        services.AddScoped<AuthorizationCatalogInitializer>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var initializer = scope.ServiceProvider.GetRequiredService<AuthorizationCatalogInitializer>();

        Assert.NotNull(initializer);
        Assert.IsType<RoleFeatureWriter>(scope.ServiceProvider.GetRequiredService<RoleFeatureWriter>());
        Assert.IsType<UserRoleAssignments>(scope.ServiceProvider.GetRequiredService<IUserRoleAssignments>());
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();
}
