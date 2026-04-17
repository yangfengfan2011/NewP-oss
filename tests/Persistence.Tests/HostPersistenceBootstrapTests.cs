using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Kernel.Extensions;
using ICP.Platform.Modules.Authorization;
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
                ["Persistence:AutoMigrate"] = "false",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        services.AddPlatformPersistence(configuration);

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
    public void AddLocalAuthSeeding_RegistersSeederAsScoped()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddLocalAuthSeeding(configuration);

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(LocalAuthSeeder));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddLocalAuthSeeding_AllowsValidatedProviderBuild()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddLogging();
        services.AddPlatformPersistence(configuration);
        services.AddModuleSystem(options =>
        {
            options.Assemblies.Add(typeof(AuthorizationModule).Assembly);
        });
        services.AddLocalAuthSeeding(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var seeder = scope.ServiceProvider.GetRequiredService<LocalAuthSeeder>();

        Assert.NotNull(seeder);
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["Persistence:AutoMigrate"] = "false",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;",
                ["LocalAuth:AdminUserId"] = "local-admin",
                ["LocalAuth:Username"] = "admin",
                ["LocalAuth:Password"] = "admin123",
                ["LocalAuth:DisplayName"] = "Local Administrator",
                ["LocalAuth:AdminRoleCode"] = "LocalAdmin",
                ["LocalAuth:AdminRoleName"] = "Local Administrator"
            })
            .Build();
}
