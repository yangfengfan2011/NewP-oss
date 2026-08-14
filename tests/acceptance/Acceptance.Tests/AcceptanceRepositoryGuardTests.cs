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

            Assert.True(clean.IsClean);
            Assert.False(firstDirty.IsClean);
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
            var ignoredBuildArtifact = Path.Combine(repository, "frontend", "packages", "shell", "dist", "assets", "entry.js");
            Directory.CreateDirectory(Path.GetDirectoryName(ignoredBuildArtifact)!);
            await File.WriteAllTextAsync(ignoredBuildArtifact, "export default true;");
            var withIgnoredArtifact = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            await File.WriteAllTextAsync(Path.Combine(repository, "untracked-input.ts"), "export const changed = true;");
            var withBuildInput = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            await File.WriteAllTextAsync(Path.Combine(repository, "untracked-input.ts"), "export const changed = false;");
            var withChangedBuildInput = await AcceptanceRepositorySnapshot.CaptureAsync(repository);

            Assert.Equal(clean, withIgnoredArtifact);
            Assert.True(clean.IsClean);
            Assert.False(withBuildInput.IsClean);
            Assert.NotEqual(clean.UntrackedBuildInputDigest, withBuildInput.UntrackedBuildInputDigest);
            Assert.NotEqual(withBuildInput.UntrackedBuildInputDigest, withChangedBuildInput.UntrackedBuildInputDigest);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task CaptureAsync_ExcludesOnlyIgnoredViteConfigTimestampArtifact()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await AddShellGitIgnoreAsync(repository);
            var clean = await AcceptanceRepositorySnapshot.CaptureAsync(repository);
            var shellRoot = Path.Combine(repository, "frontend", "packages", "shell");
            var ignoredTimestamp = Path.Combine(shellRoot, "vite.config.ts.timestamp-1234-abcd.mjs");
            await File.WriteAllTextAsync(ignoredTimestamp, "export default {};");

            Assert.Equal(clean, await AcceptanceRepositorySnapshot.CaptureAsync(repository));

            var adjacentSource = Path.Combine(shellRoot, "vite.config.ts.timestamped-1234-abcd.mjs");
            await File.WriteAllTextAsync(adjacentSource, "export default {};");
            var withAdjacentSource = await AcceptanceRepositorySnapshot.CaptureAsync(repository);

            Assert.False(withAdjacentSource.IsClean);
            Assert.Contains(
                "frontend/packages/shell/vite.config.ts.timestamped-1234-abcd.mjs",
                withAdjacentSource.UntrackedBuildInputs);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task Guard_DoesNotExcludeTrackedViteConfigTimestampArtifact()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await AddShellGitIgnoreAsync(repository);
            var trackedTimestamp = Path.Combine(
                repository,
                "frontend",
                "packages",
                "shell",
                "vite.config.ts.timestamp-1234-abcd.mjs");
            await File.WriteAllTextAsync(trackedTimestamp, "export const value = 1;");
            await RunGitAsync(
                repository,
                "add",
                "--force",
                "frontend/packages/shell/vite.config.ts.timestamp-1234-abcd.mjs");
            await RunGitAsync(repository, "commit", "-m", "track vite timestamp path");
            await using var guard = await AcceptanceRepositoryGuard.StartAsync(
                repository,
                TimeSpan.FromMilliseconds(10));

            await File.WriteAllTextAsync(trackedTimestamp, "export const value = 2;");
            await WaitUntilAsync(() => guard.HasChanged, TimeSpan.FromSeconds(3));

            Assert.False(await guard.StopAsync());
            Assert.Contains(
                guard.FirstChange!.TrackedStatus,
                entry => entry.Contains(
                    "frontend/packages/shell/vite.config.ts.timestamp-1234-abcd.mjs",
                    StringComparison.Ordinal));
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
    public async Task CaptureAsync_DoesNotExcludeUnexpectedCodeGraphBuildInput()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var codeGraphSource = Path.Combine(repository, ".codegraph", "unexpected.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(codeGraphSource)!);
            await File.WriteAllTextAsync(codeGraphSource, "internal sealed class Unexpected;");

            var snapshot = await AcceptanceRepositorySnapshot.CaptureAsync(repository);

            Assert.False(snapshot.IsClean);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task StartAsync_RejectsDirtyRepository()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(repository, "Program.cs"), "dirty");

            var dirty = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AcceptanceRepositoryGuard.StartAsync(repository));
            Assert.StartsWith("ACC_REPOSITORY_NOT_CLEAN:", dirty.Message, StringComparison.Ordinal);
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
            Assert.Contains("transient.cs", guard.FirstChange!.UntrackedBuildInputs);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task Guard_ReportsTrackedSourceStatusAndStillFailsClosed()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await using var guard = await AcceptanceRepositoryGuard.StartAsync(
                repository,
                TimeSpan.FromMilliseconds(10));
            await File.WriteAllTextAsync(Path.Combine(repository, "Program.cs"), "internal static class Changed;");
            await WaitUntilAsync(() => guard.HasChanged, TimeSpan.FromSeconds(3));

            var matched = await guard.StopAsync();

            Assert.False(matched);
            Assert.Contains(guard.FirstChange!.TrackedStatus, entry => entry.Contains("Program.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task Guard_DoesNotExcludeTrackedSourceInsideIgnoredBuildDirectory()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var trackedBuildPath = Path.Combine(repository, "frontend", "packages", "shell", "dist", "tracked.ts");
            Directory.CreateDirectory(Path.GetDirectoryName(trackedBuildPath)!);
            await File.WriteAllTextAsync(trackedBuildPath, "export const value = 1;");
            await RunGitAsync(repository, "add", "--force", "frontend/packages/shell/dist/tracked.ts");
            await RunGitAsync(repository, "commit", "-m", "track build path source");
            await using var guard = await AcceptanceRepositoryGuard.StartAsync(
                repository,
                TimeSpan.FromMilliseconds(10));

            await File.WriteAllTextAsync(trackedBuildPath, "export const value = 2;");
            await WaitUntilAsync(() => guard.HasChanged, TimeSpan.FromSeconds(3));

            Assert.False(await guard.StopAsync());
            Assert.Contains(
                guard.FirstChange!.TrackedStatus,
                entry => entry.Contains("frontend/packages/shell/dist/tracked.ts", StringComparison.Ordinal));
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task Guard_RetriesOneCaptureFailureWithoutTreatingIgnoredArtifactsAsSourceChanges()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var captures = 0;
            await using var guard = await AcceptanceRepositoryGuard.StartAsync(
                repository,
                TimeSpan.FromMilliseconds(10),
                snapshotCapture: async (root, cancellationToken) =>
                {
                    var attempt = Interlocked.Increment(ref captures);
                    if (attempt == 2)
                    {
                        throw new IOException("simulated transient git read failure");
                    }

                    return await AcceptanceRepositorySnapshot.CaptureAsync(root, cancellationToken);
                });
            var ignoredArtifact = Path.Combine(repository, ".codex-temp", "acceptance", "result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ignoredArtifact)!);
            await File.WriteAllTextAsync(ignoredArtifact, "{}");
            await Task.Delay(100);

            var matched = await guard.StopAsync();

            Assert.True(matched);
            Assert.Null(guard.FirstChange);
            Assert.True(captures >= 3);
        }
        finally
        {
            DeleteRepository(repository);
        }
    }

    [Fact]
    public async Task Guard_WhenRepositoryCannotBeCapturedTwice_FailsClosedWithDiagnostic()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var baselineCaptured = false;
            await using var guard = await AcceptanceRepositoryGuard.StartAsync(
                repository,
                TimeSpan.FromMilliseconds(10),
                snapshotCapture: async (root, cancellationToken) =>
                {
                    if (!baselineCaptured)
                    {
                        baselineCaptured = true;
                        return await AcceptanceRepositorySnapshot.CaptureAsync(root, cancellationToken);
                    }

                    throw new IOException("simulated persistent git read failure");
                });
            await WaitUntilAsync(() => guard.HasChanged, TimeSpan.FromSeconds(3));

            Assert.False(await guard.StopAsync());
            Assert.Equal("capture-error", guard.FirstChange!.Kind);
            Assert.Contains("persistent git read failure", guard.FirstChange.Diagnostic, StringComparison.Ordinal);
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
        await File.WriteAllTextAsync(
            Path.Combine(repository, ".gitignore"),
            ".codex-temp/\ndist/\nbin/\nobj/\n");
        await File.WriteAllTextAsync(Path.Combine(repository, "Program.cs"), "internal static class Program;");
        await RunGitAsync(repository, "add", ".gitignore", "Program.cs");
        await RunGitAsync(repository, "commit", "-m", "baseline");
        return repository;
    }

    private static async Task AddShellGitIgnoreAsync(string repository)
    {
        var shellRoot = Path.Combine(repository, "frontend", "packages", "shell");
        Directory.CreateDirectory(shellRoot);
        await File.WriteAllTextAsync(
            Path.Combine(shellRoot, ".gitignore"),
            "vite.config.ts.timestamp-*.mjs\n");
        await RunGitAsync(repository, "add", "frontend/packages/shell/.gitignore");
        await RunGitAsync(repository, "commit", "-m", "ignore vite timestamp artifact");
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
