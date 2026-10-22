using System.Collections.Frozen;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Runtime.ExceptionServices;

namespace ICP.Platform.Persistence.EfCore.Transactions;

/// <summary>共享 DbContext 的唯一提交所有者，同时协调已登记模型的缓存参与者。</summary>
public sealed class EfPlatformTransactionExecutor : IPlatformTransactionExecutor
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly IReadOnlyDictionary<string, IPlatformCommitParticipant> _participants;
    private readonly List<PlatformRecordChange> _changes = [];
    private HashSet<IPlatformCommitParticipant> _activeParticipants = [];
    private List<(EntityEntry Entry, EntityState State, IReadOnlyDictionary<string, object?>? Before)> _pending = [];
    private bool _preparing;
    private ExceptionDispatchInfo? _rollbackOnly;
    public bool IsActive { get; private set; }

    /// <summary>模块注册拥有者，平台只消费模型映射，不识别业务模型。</summary>
    public EfPlatformTransactionExecutor(EfPlatformDbContext dbContext, IEnumerable<PlatformCommitRegistration>? registrations = null)
    {
        _dbContext = dbContext;
        _participants = (registrations ?? []).ToDictionary(r => dbContext.ResolveModelKey(r.ModelKey), r => r.Participant, StringComparer.Ordinal);
    }
    /// <inheritdoc/>
    public bool HasParticipants(string modelKey) => _participants.ContainsKey(_dbContext.ResolveModelKey(modelKey));
    /// <inheritdoc/>
    public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> registerChanges,
        CancellationToken cancellationToken = default) => ExecuteAsync([], registerChanges, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<TResult> ExecuteAsync<TResult>(IReadOnlyCollection<string> modelKeys,
        Func<CancellationToken, ValueTask<TResult>> registerChanges, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registerChanges);
        var requested = modelKeys.Select(_dbContext.ResolveModelKey).Where(_participants.ContainsKey)
            .Select(key => _participants[key]).Distinct().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        if (IsActive)
        {
            if (requested.Any(p => !_activeParticipants.Contains(p)))
            {
                var exception = new InvalidOperationException("Nested writes must declare their models before the outer transaction begins.");
                MarkRollbackOnly(exception);
                throw exception;
            }
            try
            {
                var nested = await registerChanges(cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return nested;
            }
            catch (Exception exception)
            {
                MarkRollbackOnly(exception);
                throw;
            }
        }
        var scopes = new List<(IPlatformCommitParticipant Participant, IPlatformCommitScope Scope)>();
        try
        {
            foreach (var participant in requested) scopes.Add((participant, await participant.BeginAsync(cancellationToken)));
            _activeParticipants = requested.ToHashSet();
            _changes.Clear();
            IsActive = true;
            _dbContext.SavingChanges += CollectPending;
            _dbContext.SavedChanges += CollectSaved;
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            bool commitAttempted = false;
            bool committed = false;
            try
            {
                var result = await registerChanges(cancellationToken);
                ThrowIfRollbackOnly();
                await _dbContext.SaveChangesAsync(cancellationToken);
                _preparing = true;
                foreach (var (participant, scope) in scopes)
                    await scope.PrepareAsync(_changes.Where(c => _participants.GetValueOrDefault(c.ModelKey) == participant).ToArray(), cancellationToken);
                // 准备阶段可登记未缓存模型的事务性副作用；唯一执行器保存后才能提交，缓存模型禁止晚到变更。
                if (scopes.Count > 0) await _dbContext.SaveChangesAsync(cancellationToken);
                foreach (var (_, scope) in scopes) scope.BlockReads();
                commitAttempted = true;
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                foreach (var (_, scope) in scopes) await scope.PublishAsync();
                return result;
            }
            catch (Exception exception)
            {
                if (committed)
                {
                    foreach (var (_, scope) in scopes) scope.MarkUnavailable();
                    throw new PlatformCacheUnavailableException(string.Join(",", requested.Select(p => p.Name)), true, exception);
                }
                Exception? rollbackFailure = null;
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch (Exception cleanupException)
                {
                    // 真实提交后的完成回调也可能抛错；已完成事务的回滚错误不能覆盖原始提交诊断。
                    rollbackFailure = cleanupException;
                    exception.Data["PlatformRollbackFailure"] = cleanupException;
                }
                _dbContext.ChangeTracker.Clear();
                if (commitAttempted && rollbackFailure is not null)
                {
                    // 无法用成功回滚证明未提交时，必须先封闭旧结果，再释放提交发布屏障。
                    foreach (var (_, scope) in scopes) scope.MarkUnavailable();
                    throw new PlatformCacheUnavailableException(string.Join(",", requested.Select(p => p.Name)),
                        inner: exception, commitOutcomeUnknown: true);
                }
                throw;
            }
        }
        finally
        {
            _dbContext.SavingChanges -= CollectPending;
            _dbContext.SavedChanges -= CollectSaved;
            IsActive = false;
            _preparing = false;
            _pending.Clear();
            _activeParticipants.Clear();
            _rollbackOnly = null;
            for (var index = scopes.Count - 1; index >= 0; index--) await scopes[index].Scope.DisposeAsync();
        }
    }

    /// <inheritdoc/>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Platform transaction flush requires an active transaction.");
        }
        if (_preparing)
        {
            throw new InvalidOperationException("Platform transaction flush is unavailable after preparation begins.");
        }
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            MarkRollbackOnly(exception);
            throw;
        }
    }

    /// <inheritdoc/>
    public void RegisterChange(PlatformRecordChange change)
    {
        var key = _dbContext.ResolveModelKey(change.ModelKey);
        if (!IsActive) throw new InvalidOperationException("Changes must be registered inside a platform transaction.");
        if (_participants.TryGetValue(key, out var participant) && !_activeParticipants.Contains(participant))
            throw new InvalidOperationException("The changed model was not declared before the write.");
        if (_preparing && _participants.ContainsKey(key))
            throw new InvalidOperationException("Cache model changes cannot be registered after preparation begins.");
        _changes.Add(change with { ModelKey = key });
    }

    // SavingChanges 在审计字段盖章之后触发，因此前后值来自实际持久化实体而不是客户端 payload。
    private void CollectPending(object? sender, SavingChangesEventArgs args)
    {
        _pending = _dbContext.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => _participants.ContainsKey(e.Metadata.Name))
            .Select(e => (e, e.State, e.State == EntityState.Added ? null : Freeze(e.OriginalValues))).ToList();
        if (_preparing && _pending.Count > 0)
            throw new InvalidOperationException("Cache model changes cannot be saved after preparation begins.");
        foreach (var (entry, _, _) in _pending)
            if (!_activeParticipants.Contains(_participants[entry.Metadata.Name]))
                throw new InvalidOperationException("The changed model was not declared before the write.");
    }
    // 仅在保存成功后登记；自保存仓储与事务末尾保存进入同一变更集合。
    private void CollectSaved(object? sender, SavedChangesEventArgs args)
    {
        foreach (var (entry, state, before) in _pending)
        {
            var after = state == EntityState.Deleted ? null : Freeze(entry.CurrentValues);
            var idField = entry.Metadata.FindPrimaryKey()!.Properties.Single().Name;
            var id = Convert.ToString((after ?? before!)[idField])!;
            RegisterChange(new(entry.Metadata.Name, id, before, after));
        }
        _pending.Clear();
    }
    // 动态模型标量值从 EF PropertyValues 复制为不可变字典，禁止外部改写变更事实。
    private static IReadOnlyDictionary<string, object?> Freeze(PropertyValues values) =>
        values.Properties.ToFrozenDictionary(p => p.Name, p => values[p.Name], StringComparer.Ordinal);

    // 唯一事务 owner 保留首个嵌套失败，防止外层调用方吞掉异常后提交部分变化。
    private void MarkRollbackOnly(Exception exception) =>
        _rollbackOnly ??= ExceptionDispatchInfo.Capture(exception);

    // 根 delegate 返回后、最终保存前重新抛出首个被吞嵌套失败。
    private void ThrowIfRollbackOnly() => _rollbackOnly?.Throw();
}
