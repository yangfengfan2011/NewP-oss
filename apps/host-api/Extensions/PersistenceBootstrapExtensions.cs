using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore.Extensions;

namespace ICP.Platform.HostApi.Extensions;

public static class PersistenceBootstrapExtensions
{
    public static IServiceCollection AddPlatformPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddEfCorePersistence(configuration, GetPlatformModelDefinitions());
        return services;
    }

    public static async Task ApplyPlatformPersistenceAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var migrationRunner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
        await migrationRunner.MigrateAsync(cancellationToken);
    }

    public static IReadOnlyList<ICP.Platform.Modeling.Definitions.ModelDefinition> GetPlatformModelDefinitions() =>
        PlatformPersistenceModelCatalog.GetDefinitions();
}
