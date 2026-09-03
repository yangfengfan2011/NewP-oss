using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore.ProviderTargets;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore;

public sealed class PersistenceMigrationRunner : IPersistenceMigrationRunner, IPersistenceConsistencyChecker
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly IReadOnlyList<IPersistenceMigrationGuard> _guards;
    private readonly ModuleMigrationExecutor _moduleMigrationExecutor;
    private readonly SqlServerProviderTargetConsistencyChecker _providerTargetChecker;

    // 生产 migration runner 始终要求 bundle type 集合与编译计划精确匹配。
    public PersistenceMigrationRunner(
        EfPlatformDbContext dbContext,
        CompiledPersistencePlan plan,
        IEnumerable<IPersistenceMigrationGuard> guards,
        IProviderTargetSource providerTargetSource)
        : this(dbContext, plan, guards, providerTargetSource, requireExactMigrationTypeSet: true)
    {
    }

    // 合并式测试 bundle 可关闭额外类型拒绝，但生产公开构造始终要求 manifest 与 bundle 精确匹配。
    internal PersistenceMigrationRunner(
        EfPlatformDbContext dbContext,
        CompiledPersistencePlan plan,
        IEnumerable<IPersistenceMigrationGuard> guards,
        IProviderTargetSource providerTargetSource,
        bool requireExactMigrationTypeSet)
    {
        _dbContext = dbContext;
        Plan = plan;
        _guards = guards.ToArray();
        _moduleMigrationExecutor = new ModuleMigrationExecutor(dbContext, plan, requireExactMigrationTypeSet);
        _providerTargetChecker = new SqlServerProviderTargetConsistencyChecker(
            dbContext,
            providerTargetSource);
    }

    public bool DatabaseAhead { get; private set; }

    public CompiledPersistencePlan Plan { get; }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        DatabaseAhead = false;
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

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _moduleMigrationExecutor.VerifyAppliedHistoryAsync(cancellationToken);
        }
        catch (PersistenceHistoryException exception)
        {
            throw new PersistencePlanExecutionException(
                exception.FailureCode,
                "platform.persistence",
                exception.MigrationId,
                exception);
        }

        await _providerTargetChecker.CheckAsync(cancellationToken);
    }
}
