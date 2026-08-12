using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence.EfCore;

public sealed class PersistenceMigrationRunner : IPersistenceMigrationRunner
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly PersistenceOptions _options;
    private readonly IReadOnlyList<IPersistenceMigrationGuard> _guards;
    private readonly ModuleMigrationExecutor _moduleMigrationExecutor;

    public PersistenceMigrationRunner(
        EfPlatformDbContext dbContext,
        CompiledPersistencePlan plan,
        IOptions<PersistenceOptions> options,
        IEnumerable<IPersistenceMigrationGuard> guards)
    {
        _dbContext = dbContext;
        Plan = plan;
        _options = options.Value;
        _guards = guards.ToArray();
        _moduleMigrationExecutor = new ModuleMigrationExecutor(dbContext, plan);
    }

    public bool DatabaseAhead { get; private set; }

    public CompiledPersistencePlan Plan { get; }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        DatabaseAhead = false;
        if (!_options.AutoMigrate)
        {
            return;
        }

        try
        {
            _moduleMigrationExecutor.ValidateBeforeConnection();

            foreach (var guard in _guards)
            {
                await guard.VerifyAsync(cancellationToken);
            }

            await _moduleMigrationExecutor.ApplyAsync(cancellationToken);
        }
        catch (PersistenceHistoryException exception)
        {
            throw new PersistencePlanExecutionException(
                exception.FailureCode,
                "platform.persistence",
                exception.MigrationId,
                exception);
        }
    }
}
