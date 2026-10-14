using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class HostDeploymentRootCleanupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("NewPDeploymentCleanup_").FullName;

    [Fact]
    public async Task DeleteAsync_WaitsForOwnedDeploymentLeaseBeforeDeletingRoot()
    {
        using var lease = new FileStream(Path.Combine(_root, "host-deployment.lock"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var cleanup = HostDeploymentRootCleanup.DeleteAsync(_root, CancellationToken.None);

        Assert.False(cleanup.IsCompleted);
        Assert.True(Directory.Exists(_root));
        lease.Dispose();
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task DeleteAsync_CallerBudgetCancellationPreservesOwnedRoot()
    {
        using var lease = new FileStream(Path.Combine(_root, "host-deployment.lock"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var shutdown = new CancellationTokenSource();
        var cleanup = HostDeploymentRootCleanup.DeleteAsync(_root, shutdown.Token);
        Assert.False(cleanup.IsCompleted);

        shutdown.Cancel();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cleanup.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(shutdown.Token, failure.CancellationToken);
        Assert.True(Directory.Exists(_root));
        Assert.True(File.Exists(Path.Combine(_root, "host-deployment.lock")));
    }

    [Fact]
    public async Task DeleteAsync_PropagatesStrictDirectoryDeletionFailure()
    {
        // 部署锁已释放也不能吞掉目录中另一个真实文件的共享冲突。
        var file = Path.Combine(_root, "owned-content.bin");
        using var heldContent = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() =>
            HostDeploymentRootCleanup.DeleteAsync(_root, CancellationToken.None));

        Assert.True(Directory.Exists(_root));
        Assert.True(File.Exists(file));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
