using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence.EfCore;

public sealed class PersistenceMigrationRunner : IPersistenceMigrationRunner
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly PersistenceOptions _options;

    public PersistenceMigrationRunner(
        EfPlatformDbContext dbContext,
        IOptions<PersistenceOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        _options.AutoMigrate
            ? _dbContext.Database.MigrateAsync(cancellationToken)
            : Task.CompletedTask;
}
