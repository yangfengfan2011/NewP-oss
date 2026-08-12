using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Kernel.Tests;

/// <summary>
/// 非持久化 Host 测试显式替换正式迁移执行，避免端点契约测试接触数据库。
/// </summary>
internal sealed class TestPersistenceMigrationRunner : IPersistenceMigrationRunner
{
    public bool DatabaseAhead => false;

    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
