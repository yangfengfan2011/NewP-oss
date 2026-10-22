using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Kernel.Tests;

/// <summary>
/// 非持久化 Host 测试显式替换真实 catalog 检查，避免把端点契约测试伪装成数据库验收。
/// </summary>
internal sealed class TestPersistenceConsistencyChecker : IPersistenceConsistencyChecker
{
    public Task CheckAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
