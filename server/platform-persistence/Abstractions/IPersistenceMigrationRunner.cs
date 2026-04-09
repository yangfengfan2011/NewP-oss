namespace ICP.Platform.Persistence.Abstractions;

public interface IPersistenceMigrationRunner
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
