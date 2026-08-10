using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore.Ownership;
using ICP.Platform.Persistence.EfCore.Sql;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Extensions;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Persistence.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence.EfCore.Extensions;

public static class EfCorePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddEfCorePersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        CompiledPersistencePlan plan,
        WorktreeDatabaseIdentity databaseIdentity)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(databaseIdentity);

        services.AddSingleton(plan);
        services.AddSingleton(databaseIdentity);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddScoped<IEntityAuditStampProvider, SystemEntityAuditStampProvider>();
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

            PersistenceOptionValidation.Validate(options);

            var connectionString = ResolveConnectionString(configuration, options.Provider);
            var migrationsAssembly = EfCoreProviderOptions.GetDefaultMigrationsAssembly(options.Provider);
            EfCoreProviderOptions.Configure(builder, options.Provider, connectionString, migrationsAssembly);
            builder.ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>();
        });

        services.AddScoped<ISqlQueryExecutor, EfSqlQueryExecutor>();
        services.AddScoped<IPlatformTransactionExecutor, EfPlatformTransactionExecutor>();
        if (Enum.TryParse<PersistenceProvider>(configuration["Persistence:Provider"], true, out var provider)
            && provider is PersistenceProvider.SqlServer)
        {
            services.AddScoped<IPersistenceMigrationGuard, SqlServerDatabaseOwnershipGuard>();
        }

        services.AddScoped<IPersistenceMigrationRunner, PersistenceMigrationRunner>();

        return services;
    }

    /// <summary>
    /// 仅供尚未切换到 Canonical Module Plan 的设计期与测试调用方使用。
    /// </summary>
    public static IServiceCollection AddEfCorePersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<ModelDefinition> definitions,
        WorktreeDatabaseIdentity databaseIdentity)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var provider = Enum.TryParse<PersistenceProvider>(configuration["Persistence:Provider"], true, out var configured)
            ? configured
            : PersistenceProvider.SqlServer;
        var digest = PersistenceModelDigest.Compute(definitions);
        var plan = new CompiledPersistencePlan(
            "platform.core-legacy",
            digest,
            digest,
            provider,
            digest,
            Array.Empty<byte>(),
            definitions,
            Array.Empty<ModuleMigrationStream>());
        return services.AddEfCorePersistence(configuration, plan, databaseIdentity);
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
