using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore.Sql;
using ICP.Platform.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence.EfCore.Extensions;

public static class EfCorePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddEfCorePersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<ModelDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(definitions);

        services.AddSingleton(definitions);
        services.AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName));

        services.AddSingleton<ISqlDialect>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value;
            PersistenceOptionValidation.Validate(options);
            return CreateDialect(options.Provider);
        });

        services.AddDbContext<EfPlatformDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value;
            var modelDefinitions = sp.GetRequiredService<IReadOnlyList<ModelDefinition>>();

            PersistenceOptionValidation.Validate(options);

            var connectionString = ResolveConnectionString(configuration, options.Provider);
            var migrationsAssembly = EfCoreProviderOptions.GetDefaultMigrationsAssembly(options.Provider);
            EfCoreProviderOptions.Configure(builder, options.Provider, connectionString, migrationsAssembly);
            builder.ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>();

            _ = modelDefinitions;
        });

        services.AddScoped<ISqlQueryExecutor, EfSqlQueryExecutor>();
        services.AddScoped<IPersistenceMigrationRunner, PersistenceMigrationRunner>();

        return services;
    }
    private static ISqlDialect CreateDialect(PersistenceProvider provider) =>
        provider switch
        {
            PersistenceProvider.SqlServer => new SqlServerDialect(),
            PersistenceProvider.Oracle => new OracleDialect(),
            PersistenceProvider.MySql => new MySqlDialect(),
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
        };

    private static string ResolveConnectionString(IConfiguration configuration, PersistenceProvider provider)
    {
        var connectionName = EfCoreProviderOptions.GetConnectionStringName(provider);

        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{connectionName}' must be configured.");
        }

        return connectionString;
    }
}
