using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Persistence.EfCore.Transactions;

/// <summary>
/// 基于当前 scoped EfPlatformDbContext 的平台事务执行器。
/// </summary>
public sealed class EfPlatformTransactionExecutor : IPlatformTransactionExecutor
{
    private readonly EfPlatformDbContext _dbContext;

    public EfPlatformTransactionExecutor(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> registerChanges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registerChanges);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await registerChanges(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
