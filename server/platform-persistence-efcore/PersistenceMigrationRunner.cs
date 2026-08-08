using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence.EfCore;

public sealed class PersistenceMigrationRunner : IPersistenceMigrationRunner
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly PersistenceOptions _options;
    private readonly IReadOnlyList<IPersistenceMigrationGuard> _guards;

    public PersistenceMigrationRunner(
        EfPlatformDbContext dbContext,
        IOptions<PersistenceOptions> options,
        IEnumerable<IPersistenceMigrationGuard> guards)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _guards = guards.ToArray();
    }

    public bool DatabaseAhead { get; private set; }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        DatabaseAhead = false;
        if (!_options.AutoMigrate)
        {
            return;
        }

        foreach (var guard in _guards)
        {
            var result = await guard.VerifyAsync(cancellationToken);
            DatabaseAhead |= result.DatabaseAhead;
        }

        if (!DatabaseAhead)
        {
            await _dbContext.Database.MigrateAsync(cancellationToken);
        }
    }
}
