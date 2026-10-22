using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public sealed class AcceptanceScriptTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(Path.GetTempPath(), $"newp-acceptance-script-{Guid.NewGuid():N}");
    private const string Suite = "ICP.Platform.Acceptance.Tests.AcceptanceGateTests";

    [Theory]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "passed", 0)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver,NativeWorkspacePartEdit_UsesFormalDriver", "passed", 0)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver,NativeWorkspaceFolderEdit_UsesFormalDriver", "passed", 0)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "empty", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "skipped", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver,NativeWorkspacePartEdit_UsesFormalDriver", "partial", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver,NativeWorkspacePartEdit_UsesFormalDriver", "one-skipped", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "failed", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "duplicate", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "wrong-test", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "missing", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "malformed", 1)]
    [InlineData("NativeWorkspaceFolderEdit_UsesFormalDriver", "extra", 1)]
    public async Task Acceptance_RequiresEverySelectedTestToPass(string scenario, string report, int expectedExitCode)
    {
        InitializeFixture(scenario, report);
        var result = await RunAcceptance(scenario);
        Assert.True(result.ExitCode == expectedExitCode, $"Exit {result.ExitCode}: {result.Output}");
        Assert.Contains("acceptance-enabled=1;workspace=1;component=1;presentation=before-presentation", result.Output);
        Assert.Contains("restored=before-acceptance;before-workspace;before-component;before-presentation", result.Output);
        if (expectedExitCode != 0) Assert.Contains("Acceptance result", result.Output);
        var expected = scenario.Split(',').Distinct().Select(name => $"FullyQualifiedName={Suite}.{name}");
        Assert.Contains("filter=" + string.Join('|', expected), result.Output);
    }

    [Theory]
    [InlineData("All")]
    [InlineData("Workspace")]
    [InlineData("Personnel")]
    [InlineData("IdentityFoundation")]
    [InlineData("Authorization")]
    [InlineData("ExampleRequiredField")]
    [InlineData("NativeWorkspace*")]
    [InlineData("NativeWorkspace|FullyQualifiedName~Other")]
    [InlineData("SomeMethod,")]
    [InlineData("")]
    public async Task Acceptance_RejectsGroupsAndInvalidIdentifiersBeforeDotnet(string scenario)
    {
        InitializeFixture("unused", "missing");
        var result = await RunAcceptance(scenario);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("acceptance-enabled=", result.Output);
    }

    [Fact]
    public async Task Acceptance_RejectsRetiredGateParameter()
    {
        InitializeFixture("unused", "missing");
        var result = await RunAcceptance("Workspace", legacyGate: true);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("acceptance-enabled=", result.Output);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public async Task Acceptance_PresentationIsExplicitAndEnvironmentRestoresOnExit(int exitCode)
    {
        InitializeFixture("NativeWorkspaceFolderEdit_UsesFormalDriver", "passed");
        var result = await RunAcceptance("NativeWorkspaceFolderEdit_UsesFormalDriver", exitCode, presentation: true);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Contains("presentation=1", result.Output);
        Assert.Contains("restored=before-acceptance;before-workspace;before-component;before-presentation", result.Output);
        if (exitCode != 0)
        {
            Assert.Contains("Original scenario failure at Scenario.cs:42", result.Output);
            Assert.DoesNotContain("Acceptance result", result.Output);
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(_fixtureRoot))
        {
            Directory.Delete(_fixtureRoot, recursive: true);
        }
    }

    private void InitializeFixture(string scenario, string report)
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
            Write-Output "acceptance-enabled=$env:NEWP_RUN_ACCEPTANCE;workspace=$env:NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE;component=$env:NEWP_RUN_NATIVE_COMPONENT_ACCEPTANCE;presentation=$env:NEWP_ACCEPTANCE_PRESENTATION"
            Write-Output ('filter=' + $args[[Array]::IndexOf($args, '--filter') + 1])
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
        var scenarios = scenario.Split(',').Distinct().ToArray();
        var results = new XElement(ns + "Results");
        foreach (var selected in scenarios)
        {
            if (report == "empty" || (report == "partial" && selected != scenarios[0]))
            {
                continue;
            }
            var outcome = report == "failed" ? "Failed"
                : report == "skipped" || (report == "one-skipped" && selected == scenarios[0]) ? "NotExecuted" : "Passed";
            var testResult = new XElement(ns + "UnitTestResult",
                new XAttribute("testName", $"{Suite}.{(report == "wrong-test" ? "AnotherScenario" : selected)}"),
                new XAttribute("outcome", outcome));
            results.Add(testResult);
            if (report == "duplicate")
            {
                results.Add(new XElement(testResult));
            }
        }
        if (report == "extra") results.Add(new XElement(ns + "UnitTestResult", new XAttribute("testName", Suite + ".UnselectedScenario"), new XAttribute("outcome", "Passed")));
        new XDocument(new XElement(ns + "TestRun", results)).Save(reportPath);
    }

    private async Task<(int ExitCode, string Output)> RunAcceptance(string scenario, int dotnetExitCode = 0, bool presentation = false, bool legacyGate = false)
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
        var wrapper = Path.Combine(_fixtureRoot, "run.ps1");
        File.WriteAllText(wrapper, """
            $ErrorActionPreference = 'Stop'
            $env:NEWP_RUN_ACCEPTANCE = 'before-acceptance'
            $env:NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE = 'before-workspace'
            $env:NEWP_RUN_NATIVE_COMPONENT_ACCEPTANCE = 'before-component'
            $env:NEWP_ACCEPTANCE_PRESENTATION = 'before-presentation'
            $selection = if ($env:NEWP_FAKE_LEGACY -eq '1') { @{ Gate = $env:NEWP_FAKE_SCENARIO } } else { @{ Scenario = $env:NEWP_FAKE_SCENARIO } }
            if ($env:NEWP_FAKE_PRESENTATION -eq '1') { $selection.Presentation = $true }
            & (Join-Path $PSScriptRoot 'scripts/accept.ps1') @selection -NoBuild
            $code = $LASTEXITCODE
            Write-Output "restored=$env:NEWP_RUN_ACCEPTANCE;$env:NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE;$env:NEWP_RUN_NATIVE_COMPONENT_ACCEPTANCE;$env:NEWP_ACCEPTANCE_PRESENTATION"
            exit $code
            """);
        foreach (var argument in new[] { "-NoProfile", "-File", wrapper })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.StartInfo.Environment["PATH"] = Path.Combine(_fixtureRoot, "shims") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        process.StartInfo.Environment["NEWP_FAKE_TRX"] = Path.Combine(_fixtureRoot, "input.trx");
        process.StartInfo.Environment["NEWP_FAKE_DOTNET_EXIT"] = dotnetExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        process.StartInfo.Environment["NEWP_FAKE_SCENARIO"] = scenario;
        process.StartInfo.Environment["NEWP_FAKE_PRESENTATION"] = presentation ? "1" : "0";
        process.StartInfo.Environment["NEWP_FAKE_LEGACY"] = legacyGate ? "1" : "0";
        Assert.True(process.Start());
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }
}
