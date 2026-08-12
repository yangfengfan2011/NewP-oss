namespace ICP.Platform.Persistence.Abstractions;

public interface IPersistenceMigrationRunner
{
    /// <summary>
    /// 兼容现有插件激活交接端口。统一 history 超前现在会抛出阻断异常，
    /// 因此成功完成的持久化启动必须返回 false。
    /// </summary>
    bool DatabaseAhead { get; }

    Task MigrateAsync(CancellationToken cancellationToken = default);
}
