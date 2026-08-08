namespace ICP.Platform.Persistence.Abstractions;

public interface IPersistenceMigrationRunner
{
    /// <summary>
    /// 获取最近一次执行是否识别到只读的 database-ahead 状态。
    /// </summary>
    bool DatabaseAhead { get; }

    Task MigrateAsync(CancellationToken cancellationToken = default);
}
