using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Persistence.EfCore.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class EfCorePersistenceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEfCorePersistence_RegistersRequestedProviderServices()
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

        services.AddEfCorePersistence(configuration, BuildDefinitions());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var dialect = scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        var executor = scope.ServiceProvider.GetRequiredService<ISqlQueryExecutor>();
        var runner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
        var dbContext = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();

        Assert.Equal("SqlServer", dialect.Name);
        Assert.IsType<EfSqlQueryExecutor>(executor);
        Assert.IsType<PersistenceMigrationRunner>(runner);
        Assert.NotNull(dbContext);
        Assert.NotNull(dbContext.Model.FindEntityType("ConfigItem"));
    }

    private static IReadOnlyList<ModelDefinition> BuildDefinitions() =>
        new[]
        {
            new ModelDefinition(
                "ConfigItem",
                "config_items",
                ModelKind.Entity,
                new[]
                {
                    new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
                    new FieldDefinition("Key", "config_key", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
                },
                Array.Empty<IndexDefinition>())
        };
}
