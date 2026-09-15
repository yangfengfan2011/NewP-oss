using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public sealed class AcceptanceScriptTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(Path.GetTempPath(), $"newp-acceptance-script-{Guid.NewGuid():N}");
    private const string Suite = "ICP.Platform.Acceptance.Tests.AcceptanceGateTests";

    [Theory]
    [InlineData("Personnel", "passed", 0)]
    [InlineData("All", "passed", 0)]
    [InlineData("Personnel", "empty", 1)]
    [InlineData("Personnel", "skipped", 1)]
    [InlineData("All", "skipped", 1)]
    [InlineData("All", "partial", 1)]
    [InlineData("All", "one-skipped", 1)]
    [InlineData("Personnel", "failed", 1)]
    [InlineData("Personnel", "duplicate", 1)]
    [InlineData("Personnel", "wrong-test", 1)]
    [InlineData("Personnel", "missing", 1)]
    [InlineData("Personnel", "malformed", 1)]
    public async Task Acceptance_RequiresEverySelectedTestToPass(string gate, string report, int expectedExitCode)
    {
        InitializeFixture(gate, report);

        var result = await RunAcceptance(gate);

        Assert.True(result.ExitCode == expectedExitCode, $"Exit {result.ExitCode}: {result.Output}");
        Assert.Contains("acceptance-enabled=1", result.Output);
        if (expectedExitCode != 0)
        {
            Assert.Contains("Acceptance result", result.Output);
        }
    }

    [Fact]
    public async Task Acceptance_WhenDotnetFails_PreservesExitCodeAndOriginalDiagnostic()
    {
        InitializeFixture("Personnel", "missing");

        var result = await RunAcceptance("Personnel", dotnetExitCode: 37);

        Assert.Equal(37, result.ExitCode);
        Assert.Contains("Original scenario failure at Scenario.cs:42", result.Output);
        Assert.DoesNotContain("Acceptance result", result.Output);
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureRoot))
        {
            Directory.Delete(_fixtureRoot, recursive: true);
        }
    }

    private void InitializeFixture(string gate, string report)
    {
        var scriptsRoot = Path.Combine(_fixtureRoot, "scripts");
        var shimRoot = Path.Combine(_fixtureRoot, "shims");
        Directory.CreateDirectory(scriptsRoot);
        Directory.CreateDirectory(shimRoot);
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "scripts", "accept.ps1")))
        {
            current = current.Parent;
        }
        Assert.NotNull(current);
        File.Copy(Path.Combine(current.FullName, "scripts", "accept.ps1"), Path.Combine(scriptsRoot, "accept.ps1"));
        File.WriteAllText(Path.Combine(shimRoot, "dotnet.ps1"), """
            Write-Output "acceptance-enabled=$env:NEWP_RUN_ACCEPTANCE"
            if ($env:NEWP_FAKE_DOTNET_EXIT -ne '0') {
                [Console]::Error.WriteLine('Original scenario failure at Scenario.cs:42')
                exit ([int]$env:NEWP_FAKE_DOTNET_EXIT)
            }
            $directoryIndex = [Array]::IndexOf($args, '--results-directory')
            if ($directoryIndex -ge 0 -and (Test-Path -LiteralPath $env:NEWP_FAKE_TRX)) {
                $destination = $args[$directoryIndex + 1]
                New-Item -ItemType Directory -Path $destination -Force | Out-Null
                Copy-Item -LiteralPath $env:NEWP_FAKE_TRX -Destination (Join-Path $destination 'acceptance.trx')
            }
            exit 0
            """);

        if (report == "missing")
        {
            return;
        }
        var reportPath = Path.Combine(_fixtureRoot, "input.trx");
        if (report == "malformed")
        {
            File.WriteAllText(reportPath, "<TestRun");
            return;
        }
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        string[] gates = gate == "All" ? ["Authorization", "IdentityFoundation", "Personnel", "ExampleRequiredField"] : [gate];
        var results = new XElement(ns + "Results");
        foreach (var selected in gates)
        {
            if (report == "empty" || (report == "partial" && selected != "Personnel"))
            {
                continue;
            }
            var outcome = report == "failed" ? "Failed"
                : report == "skipped" || (report == "one-skipped" && selected == "Personnel") ? "NotExecuted" : "Passed";
            var testResult = new XElement(ns + "UnitTestResult",
                new XAttribute("testName", $"{Suite}.{(report == "wrong-test" ? "AnotherGate" : selected)}_CompletesEveryScenario"),
                new XAttribute("outcome", outcome));
            results.Add(testResult);
            if (report == "duplicate")
            {
                results.Add(new XElement(testResult));
            }
        }
        new XDocument(new XElement(ns + "TestRun", results)).Save(reportPath);
    }

    private async Task<(int ExitCode, string Output)> RunAcceptance(string gate, int dotnetExitCode = 0)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("pwsh.exe")
            {
                WorkingDirectory = _fixtureRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(_fixtureRoot, "scripts", "accept.ps1"), "-Gate", gate, "-NoBuild" })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.StartInfo.Environment["PATH"] = Path.Combine(_fixtureRoot, "shims") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        process.StartInfo.Environment["NEWP_FAKE_TRX"] = Path.Combine(_fixtureRoot, "input.trx");
        process.StartInfo.Environment["NEWP_FAKE_DOTNET_EXIT"] = dotnetExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }
}
