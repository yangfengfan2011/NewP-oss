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

        try
        {
            var runner = new AcceptanceSkeletonRunner();

            var outcome = await runner.RunAsync(
                GetRepositoryRoot(),
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
            Assert.Equal(await RunGitAsync("rev-parse", "HEAD"), result.GetProperty("commit").GetString());
            Assert.Equal(await RunGitAsync("rev-parse", "HEAD^{tree}"), result.GetProperty("treeHash").GetString());

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

    private static async Task<string> RunGitAsync(params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return (await output).Trim();
    }
}
