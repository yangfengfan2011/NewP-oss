using System.Diagnostics;
using System.Text.Json;
using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class AcceptanceSkeletonRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenSelectedGateIsNotImplemented_WritesParseableNonGreenResult()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"newp-acceptance-run-{Guid.NewGuid():N}");
        var repositoryRoot = await CreateRepositoryAsync();

        try
        {
            var runner = new AcceptanceSkeletonRunner();
            var expectedCommit = await RunGitAsync(repositoryRoot, "rev-parse", "HEAD");
            var expectedTree = await RunGitAsync(repositoryRoot, "rev-parse", "HEAD^{tree}");

            var outcome = await runner.RunAsync(
                repositoryRoot,
                outputRoot,
                AcceptanceGateSelection.Plugin);

            Assert.Equal(1, outcome.ExitCode);
            Assert.True(File.Exists(outcome.ResultPath));
            Assert.True(File.Exists(Path.Combine(outputRoot, "current-result.json")));

            using var resultJson = JsonDocument.Parse(await File.ReadAllTextAsync(outcome.ResultPath));
            var result = resultJson.RootElement;

            Assert.Equal("1.0", result.GetProperty("schemaVersion").GetString());
            Assert.NotEqual("passed", result.GetProperty("verdict").GetString());
            Assert.Equal(AcceptanceFailureCodes.GateNotImplemented, result.GetProperty("failureCode").GetString());
            Assert.Single(result.GetProperty("gates").EnumerateObject());
            Assert.Equal(expectedCommit, result.GetProperty("commit").GetString());
            Assert.Equal(expectedTree, result.GetProperty("treeHash").GetString());

            var runDirectory = Path.GetDirectoryName(outcome.ResultPath)!;
            var eventLines = await File.ReadAllLinesAsync(Path.Combine(runDirectory, "events.jsonl"));
            Assert.NotEmpty(eventLines);
            Assert.All(eventLines, line => JsonDocument.Parse(line));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
            DeleteRepository(repositoryRoot);
        }
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    private static async Task<string> CreateRepositoryAsync()
    {
        var repository = Path.Combine(Path.GetTempPath(), "newp-acceptance-skeleton-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        await RunGitCommandAsync(repository, "init");
        await RunGitCommandAsync(repository, "config", "user.email", "acceptance@example.invalid");
        await RunGitCommandAsync(repository, "config", "user.name", "Acceptance Tests");
        await File.WriteAllTextAsync(Path.Combine(repository, ".gitignore"), ".codex-temp/\n");
        await File.WriteAllTextAsync(Path.Combine(repository, "Program.cs"), "internal static class Program;");
        await RunGitCommandAsync(repository, "add", ".gitignore", "Program.cs");
        await RunGitCommandAsync(repository, "commit", "-m", "baseline");
        return repository;
    }

    private static async Task<string> RunGitAsync(string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return (await output).Trim();
    }

    private static async Task RunGitCommandAsync(string repositoryRoot, params string[] arguments) =>
        _ = await RunGitAsync(repositoryRoot, arguments);

    private static void DeleteRepository(string repository)
    {
        foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(repository, recursive: true);
    }
}
