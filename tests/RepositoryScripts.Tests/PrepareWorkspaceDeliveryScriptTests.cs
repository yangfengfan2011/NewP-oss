using System.Diagnostics;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public sealed class PrepareWorkspaceDeliveryScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"newp-prepare-delivery-{Guid.NewGuid():N}");

    [Fact]
    public async Task Prepare_RejectsBaselineFromAnotherWorktreeBeforeAnyBuild()
    {
        var checkout = CreateRepositoryRoot("checkout");
        var baseline = Path.Combine(checkout, ".codex-temp", "baseline-host");
        Directory.CreateDirectory(baseline);
        var worktree = CreateRepositoryRoot("worktree");
        var scripts = Directory.CreateDirectory(Path.Combine(worktree, "scripts")).FullName;
        File.Copy(Path.Combine(FindRepositoryRoot(), "scripts", "prepare-workspace-delivery.ps1"),
            Path.Combine(scripts, "prepare-workspace-delivery.ps1"));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("pwsh.exe")
            {
                WorkingDirectory = worktree, UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            }
        };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(scripts, "prepare-workspace-delivery.ps1"), "-BaselineHostRoot", baseline })
            process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout + await stderr;

        Assert.True(process.ExitCode != 0, output);
        Assert.Contains("PERSISTENCE_DATABASE_IDENTITY_MISMATCH", output);
        Assert.False(Directory.Exists(Path.Combine(worktree, ".codex-temp", "workspace-delivery")), "检查必须发生在任何构建或运行目录创建之前。");
    }

    // 与 Host 的仓库根识别一致：同时含 apps、server、frontend/modules。
    private string CreateRepositoryRoot(string name)
    {
        var path = Path.Combine(_root, name);
        foreach (var child in new[] { "apps", "server", Path.Combine("frontend", "modules") }) Directory.CreateDirectory(Path.Combine(path, child));
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ICP-Platform.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("找不到仓库根。");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
