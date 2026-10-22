using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore;

public static class EfCoreProviderOptions
{
    public static void Configure(
        DbContextOptionsBuilder builder,
        PersistenceProvider provider,
        string connectionString,
        string? migrationsAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        switch (provider)
        {
            case PersistenceProvider.SqlServer:
                builder.UseSqlServer(connectionString, options =>
                {
                    if (!string.IsNullOrWhiteSpace(migrationsAssembly))
                    {
                        options.MigrationsAssembly(migrationsAssembly);
                    }
                });
                break;
            case PersistenceProvider.Oracle:
                builder.UseOracle(connectionString, options =>
                {
                    if (!string.IsNullOrWhiteSpace(migrationsAssembly))
                    {
                        options.MigrationsAssembly(migrationsAssembly);
                    }
                });
                break;
            case PersistenceProvider.MySql:
                builder.UseMySQL(connectionString, options =>
                {
                    if (!string.IsNullOrWhiteSpace(migrationsAssembly))
                    {
                        options.MigrationsAssembly(migrationsAssembly);
                    }
                });
                break;
            default:
                throw new InvalidOperationException($"Unsupported provider: {provider}");
        }
    }

    public static string GetConnectionStringName(PersistenceProvider provider) =>
        provider switch
        {
            PersistenceProvider.SqlServer => "SqlServer",
            PersistenceProvider.Oracle => "Oracle",
            PersistenceProvider.MySql => "MySql",
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
        };

    public static string GetDefaultMigrationsAssembly(PersistenceProvider provider) =>
        provider switch
        {
            PersistenceProvider.SqlServer => "PlatformPersistence.EfCore.SqlServerMigrations",
            PersistenceProvider.Oracle => "PlatformPersistence.EfCore.OracleMigrations",
            PersistenceProvider.MySql => "PlatformPersistence.EfCore.MySqlMigrations",
            _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
        };
}
