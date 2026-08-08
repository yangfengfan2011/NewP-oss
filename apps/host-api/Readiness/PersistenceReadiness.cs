namespace ICP.Platform.HostApi.Readiness;

public static class PersistenceReadinessFailureCodes
{
    public const string NotInitialized = "PERSISTENCE_NOT_INITIALIZED";
    public const string BootstrapFailed = "PERSISTENCE_BOOTSTRAP_FAILED";
}

/// <summary>
/// 保存启动期持久化结果；readiness 只读取该快照，不在 HTTP 请求中连接数据库或轮询。
/// </summary>
public sealed class PersistenceReadinessState
{
    private Snapshot _snapshot = new(
        ReadinessState.Starting,
        PersistenceReadinessFailureCodes.NotInitialized,
        "Persistence bootstrap has not completed.");

    internal Snapshot Current => Volatile.Read(ref _snapshot);

    public void MarkStarting() =>
        Interlocked.Exchange(
            ref _snapshot,
            new Snapshot(
                ReadinessState.Starting,
                PersistenceReadinessFailureCodes.NotInitialized,
                "Persistence bootstrap has not completed."));

    public void MarkReady(bool databaseAhead) =>
        Interlocked.Exchange(
            ref _snapshot,
            new Snapshot(
                ReadinessState.Ready,
                null,
                databaseAhead
                    ? "Persistence ready (database-ahead; no Down migration executed)."
                    : "Persistence ready."));

    public void MarkFailed(string? failureCode = null) =>
        Interlocked.Exchange(
            ref _snapshot,
            new Snapshot(
                ReadinessState.Failed,
                string.IsNullOrWhiteSpace(failureCode)
                    ? PersistenceReadinessFailureCodes.BootstrapFailed
                    : failureCode,
                "Persistence bootstrap failed."));

    internal sealed record Snapshot(
        ReadinessState State,
        string? FailureCode,
        string Diagnostic);
}

public sealed class PersistenceReadinessContributor : IReadinessContributor
{
    private readonly PersistenceReadinessState _state;

    public PersistenceReadinessContributor(PersistenceReadinessState state)
    {
        _state = state;
    }

    public string Name => "database";

    public int Order => 100;

    public bool IsRequired => true;

    public ValueTask<ReadinessContribution> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _state.Current;
        return ValueTask.FromResult(new ReadinessContribution(
            snapshot.State,
            snapshot.FailureCode,
            snapshot.Diagnostic));
    }
}
