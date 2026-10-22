namespace ICP.Platform.Kernel.Tests;

// Host.StopAsync 不等待 Program 的 Main 作用域退出；私有部署目录须等其锁释放后才能删除。
internal static class HostDeploymentRootCleanup
{
    // 调用方在停止 Host 前创建预算；这里只消费同一 token，不重建或延长 shutdown 预算。
    internal static async Task DeleteAsync(string ownedRoot, CancellationToken shutdownToken)
    {
        while (!DeploymentLockReleased(ownedRoot))
        {
            await Task.Delay(10, shutdownToken);
        }

        if (Directory.Exists(ownedRoot))
        {
            Directory.Delete(ownedRoot, recursive: true);
        }
    }

    private static bool DeploymentLockReleased(string ownedRoot)
    {
        var path = Path.Combine(ownedRoot, "host-deployment.lock");
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            using var lease = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException failure) when ((failure.HResult & 0xffff) is 32 or 33)
        {
            return false;
        }
    }
}
