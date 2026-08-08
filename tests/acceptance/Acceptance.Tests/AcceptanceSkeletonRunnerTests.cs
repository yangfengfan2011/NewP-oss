using System.Text.Json;
using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class AcceptanceSkeletonRunnerTests
{
    [Theory]
    [InlineData(AcceptanceGateSelection.Personnel, 1)]
    [InlineData(AcceptanceGateSelection.Plugin, 1)]
    [InlineData(AcceptanceGateSelection.All, 2)]
    public async Task RunAsync_WritesParseableNonGreenResult(
        AcceptanceGateSelection selection,
        int expectedGateCount)
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"newp-acceptance-run-{Guid.NewGuid():N}");

        try
        {
            var runner = new AcceptanceSkeletonRunner();

            var outcome = await runner.RunAsync(GetRepositoryRoot(), outputRoot, selection);

            Assert.Equal(1, outcome.ExitCode);
            Assert.True(File.Exists(outcome.ResultPath));
            Assert.True(File.Exists(Path.Combine(outputRoot, "current-result.json")));

            using var resultJson = JsonDocument.Parse(await File.ReadAllTextAsync(outcome.ResultPath));
            var result = resultJson.RootElement;

            Assert.Equal("1.0", result.GetProperty("schemaVersion").GetString());
            Assert.NotEqual("passed", result.GetProperty("verdict").GetString());
            Assert.Equal(AcceptanceFailureCodes.GateNotImplemented, result.GetProperty("failureCode").GetString());
            Assert.Equal(expectedGateCount, result.GetProperty("gates").EnumerateObject().Count());
            Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("commit").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("treeHash").GetString()));

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
}
