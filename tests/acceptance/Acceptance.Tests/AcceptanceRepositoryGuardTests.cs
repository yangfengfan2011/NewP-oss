using System.Diagnostics;
using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class AcceptanceRepositoryGuardTests
{
    [Fact]
    public async Task CaptureAsync_BindsHeadTreeAndTrackedDirtyContent()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var clean = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            await File.WriteAllTextAsync(Path.Combine(repository, "Program.cs"), "first dirty content");
            var firstDirty = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            await File.WriteAllTextAsync(Path.Combine(repository, "Program.cs"), "second dirty content");
            var secondDirty = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            await RunGitAsync(repository, "add", "Program.cs");
            await RunGitAsync(repository, "commit", "-m", "change tree");
            var nextCommit = await AcceptanceRepositorySnapshot.CaptureAsync(repository);

            Assert.NotEqual(clean.TrackedStateDigest, firstDirty.TrackedStateDigest);
            Assert.NotEqual(firstDirty.TrackedStateDigest, secondDirty.TrackedStateDigest);
            Assert.NotEqual(clean.HeadCommit, nextCommit.HeadCommit);
            Assert.NotEqual(clean.HeadTree, nextCommit.HeadTree);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task CaptureAsync_BindsBuildInputsButExcludesIgnoredAcceptanceArtifacts()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var clean = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            var ignoredArtifact = Path.Combine(repository, ".codex-temp", "acceptance", "runs", "run", "result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ignoredArtifact)!);
            await File.WriteAllTextAsync(ignoredArtifact, "{}");
            var withIgnoredArtifact = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            await File.WriteAllTextAsync(Path.Combine(repository, "untracked-input.ts"), "export const changed = true;");
            var withBuildInput = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            await File.WriteAllTextAsync(Path.Combine(repository, "untracked-input.ts"), "export const changed = false;");
            var withChangedBuildInput = await AcceptanceRepositorySnapshot.CaptureAsync(repository);

            Assert.Equal(clean, withIgnoredArtifact);
            Assert.NotEqual(clean.UntrackedBuildInputDigest, withBuildInput.UntrackedBuildInputDigest);
            Assert.NotEqual(withBuildInput.UntrackedBuildInputDigest, withChangedBuildInput.UntrackedBuildInputDigest);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task CaptureAsync_ExcludesCodeGraphRuntimeLock()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var clean = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            var codeGraphLock = Path.Combine(repository, ".codegraph", "codegraph.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(codeGraphLock)!);
            await File.WriteAllTextAsync(codeGraphLock, "runtime lock");

            var withCodeGraphLock = await AcceptanceRepositorySnapshot.CaptureAsync(repository);

            Assert.Equal(clean, withCodeGraphLock);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task Guard_LatchesTransientRepositoryChangeEvenWhenFinalSnapshotMatches()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await using var guard = await AcceptanceRepositoryGuard.StartAsync(
                repository,
                TimeSpan.FromMilliseconds(10));
            var transientInput = Path.Combine(repository, "transient.cs");
            await File.WriteAllTextAsync(transientInput, "internal sealed class Transient;");
            await WaitUntilAsync(() => guard.HasChanged, TimeSpan.FromSeconds(3));
            File.Delete(transientInput);

            var matched = await guard.StopAsync();

            Assert.False(matched);
            Assert.Equal(guard.Baseline, await AcceptanceRepositorySnapshot.CaptureAsync(repository));
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    private static async Task<string> CreateRepositoryAsync()
    {
        var repository = Path.Combine(Path.GetTempPath(), "newp-acceptance-repository-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        await RunGitAsync(repository, "init");
        await RunGitAsync(repository, "config", "user.email", "acceptance@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Acceptance Tests");
        await File.WriteAllTextAsync(Path.Combine(repository, ".gitignore"), ".codex-temp/\n");
        await File.WriteAllTextAsync(Path.Combine(repository, "Program.cs"), "internal static class Program;");
        await RunGitAsync(repository, "add", ".gitignore", "Program.cs");
        await RunGitAsync(repository, "commit", "-m", "baseline");
        return repository;
    }

    private static async Task RunGitAsync(string repository, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git failed: {await output} {await error}");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Repository guard did not observe the change.");
            await Task.Delay(10);
        }
    }

    private static void DeleteRepository(string repository)
    {
        foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(repository, recursive: true);
    }
}
