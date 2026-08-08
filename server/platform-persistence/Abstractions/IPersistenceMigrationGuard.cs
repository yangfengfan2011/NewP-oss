namespace ICP.Platform.Persistence.Abstractions;

/// <summary>
/// 在 provider migration 执行任何 schema DDL 前验证数据库归属与历史边界。
/// </summary>
public interface IPersistenceMigrationGuard
{
    Task<PersistenceMigrationGuardResult> VerifyAsync(CancellationToken cancellationToken = default);
}

public readonly record struct PersistenceMigrationGuardResult(bool DatabaseAhead);
