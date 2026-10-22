using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public sealed class VerificationPipelineTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        $"newp-verification-pipeline-{Guid.NewGuid():N}");

    [Fact]
    public void VerificationScript_DeclaresOneNoBuildPreflightBetweenReleaseBuildAndTests()
    {
        var verification = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "verify.ps1"));
        const string preflightStage = "$currentStage = \"migration-manifest-preflight\"";
        var preflightIndex = verification.IndexOf(preflightStage, StringComparison.Ordinal);
        var nextStageIndex = verification.IndexOf(
            "$currentStage = \"model-dto-sync-check\"",
            preflightIndex,
            StringComparison.Ordinal);
        var preflightBlock = verification[preflightIndex..nextStageIndex];

        Assert.True(preflightIndex > verification.LastIndexOf("$currentStage = \"dotnet-build\"", StringComparison.Ordinal));
        Assert.True(preflightIndex < verification.IndexOf("$currentStage = \"dotnet-test\"", StringComparison.Ordinal));
        Assert.True(preflightIndex < verification.IndexOf("foreach ($scenario in $AcceptanceScenario)", StringComparison.Ordinal));
        Assert.Equal(1, CountOccurrences(verification, preflightStage));
        Assert.Equal(2, CountOccurrences(preflightBlock, "\"--no-build\""));
        Assert.DoesNotContain("\"build\"", preflightBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationPipeline_RunsOneBuildThenNoBuildPreflightBeforeTestsAndAcceptance()
    {
        var fixture = InitializeFixture(preflightFails: false);

        var result = RunVerification(fixture);

        Assert.True(
            result.ExitCode == 0,
            $"Verification failed.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        var commands = File.ReadAllLines(fixture.CommandLogPath);
        var buildIndex = FindSingle(commands, "dotnet build ICP-Platform.sln --configuration Release");
        var preflightIndex = FindSingle(commands, "migration-manifest/MigrationManifest.csproj");
        var dotnetTestIndex = FindSingle(commands, "dotnet test ICP-Platform.sln");
        var frontendTestIndex = FindSingle(commands, "corepack pnpm --dir frontend test");
        var acceptanceIndex = FindSingle(commands, "acceptance -Scenario NativeWorkspaceFolderEdit_UsesFormalDriver -NoBuild");

        Assert.True(buildIndex < preflightIndex);
        Assert.True(preflightIndex < dotnetTestIndex);
        Assert.True(preflightIndex < frontendTestIndex);
        Assert.True(preflightIndex < acceptanceIndex);
        Assert.Single(commands, static command => command.StartsWith("dotnet build ", StringComparison.Ordinal));
        Assert.Contains("--configuration Release --no-build --no-restore -- check --all --no-build", commands[preflightIndex], StringComparison.Ordinal);
        Assert.DoesNotContain(commands, static command => command.Contains("frontend lint", StringComparison.Ordinal));
    }

    [Fact]
    public void FullVerification_RunsLintBeforeFrontendTestsBuildAndAcceptance()
    {
        var fixture = InitializeFixture(preflightFails: false);

        var result = RunVerification(fixture, quick: false);

        Assert.True(result.ExitCode == 0, result.StdOut + result.StdErr);
        var commands = File.ReadAllLines(fixture.CommandLogPath);
        var lintIndex = FindSingle(commands, "corepack pnpm --dir frontend lint");
        Assert.True(lintIndex < FindSingle(commands, "corepack pnpm --dir frontend test"));
        Assert.True(lintIndex < FindSingle(commands, "corepack pnpm --dir frontend build"));
        Assert.True(lintIndex < FindSingle(commands, "acceptance -Scenario NativeWorkspaceFolderEdit_UsesFormalDriver -NoBuild"));
    }

    [Fact]
    public void FullVerification_WhenLintFails_StopsAndPreservesDiagnostic()
    {
        var fixture = InitializeFixture(preflightFails: false, lintFails: true);

        var result = RunVerification(fixture, quick: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("fixture.tsx:12 react-hooks/rules-of-hooks", result.StdOut + result.StdErr);
        var commands = File.ReadAllLines(fixture.CommandLogPath);
        Assert.DoesNotContain(commands, static command => command.Contains("frontend test", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, static command => command.Contains("frontend build", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, static command => command.StartsWith("acceptance ", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(File.ReadAllText(fixture.CurrentResultPath));
        Assert.Equal("frontend-lint", document.RootElement.GetProperty("failureStage").GetString());
    }

    [Fact]
    public void VerificationPipeline_WhenPreflightFails_StopsAndPreservesOriginalDiagnostic()
    {
        var fixture = InitializeFixture(preflightFails: true);

        var result = RunVerification(fixture);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(Fixture.PreflightDiagnostic, result.StdOut + result.StdErr, StringComparison.Ordinal);
        var commands = File.ReadAllLines(fixture.CommandLogPath);
        Assert.Single(commands, static command => command.StartsWith("dotnet build ", StringComparison.Ordinal));
        Assert.Single(commands, static command => command.Contains("migration-manifest/MigrationManifest.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, static command => command.StartsWith("dotnet test ", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, static command => command.StartsWith("corepack ", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, static command => command.StartsWith("acceptance ", StringComparison.Ordinal));

        using var resultDocument = JsonDocument.Parse(File.ReadAllText(fixture.CurrentResultPath));
        Assert.Equal("failed", resultDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal("migration-manifest-preflight", resultDocument.RootElement.GetProperty("failureStage").GetString());
        var stage = resultDocument.RootElement.GetProperty("stages")
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "migration-manifest-preflight");
        var stageLogPath = Path.Combine(
            fixture.Root,
            stage.GetProperty("logPath").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains(Fixture.PreflightDiagnostic, File.ReadAllText(stageLogPath), StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationPipeline_PassesOnlyExplicitScenariosAndHasNoAcceptanceDefault()
    {
        var fixture = InitializeFixture(preflightFails: false);
        var result = RunVerification(fixture, scenarios: null);
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(File.ReadAllLines(fixture.CommandLogPath), command => command.StartsWith("acceptance "));
        File.WriteAllText(fixture.CommandLogPath, string.Empty);
        result = RunVerification(fixture, scenarios: "NativeWorkspaceFolderEdit_UsesFormalDriver,NativeWorkspacePartEdit_UsesFormalDriver");
        Assert.Equal(0, result.ExitCode);
        var commands = File.ReadAllLines(fixture.CommandLogPath);
        Assert.Contains("acceptance -Scenario NativeWorkspaceFolderEdit_UsesFormalDriver -NoBuild", commands);
        Assert.Contains("acceptance -Scenario NativeWorkspacePartEdit_UsesFormalDriver -NoBuild", commands);
        Assert.Equal(2, commands.Count(command => command.StartsWith("acceptance ")));
    }

    [Fact]
    public void VerificationPipeline_RejectsRetiredAcceptanceGate()
    {
        var fixture = InitializeFixture(preflightFails: false);
        var result = RunVerification(fixture, scenarios: "Workspace", legacyGate: true);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(fixture.CommandLogPath));
    }
    [Fact]
    public void VerificationPipeline_WhenSelectedScenarioFails_StopsBeforeNextScenario()
    {
        var fixture = InitializeFixture(preflightFails: false);
        File.AppendAllText(Path.Combine(fixture.Root, "scripts", "accept.ps1"), "exit 37\r\n");
        var result = RunVerification(fixture, scenarios: "NativeWorkspaceFolderEdit_UsesFormalDriver,NativeWorkspacePartEdit_UsesFormalDriver");
        Assert.Equal(1, result.ExitCode);
        var commands = File.ReadAllLines(fixture.CommandLogPath);
        Assert.Single(commands, command => command.StartsWith("acceptance "));
        using var document = JsonDocument.Parse(File.ReadAllText(fixture.CurrentResultPath));
        Assert.Equal("acceptance-NativeWorkspaceFolderEdit_UsesFormalDriver", document.RootElement.GetProperty("failureStage").GetString());
    }

    [Theory]
    [InlineData("All")]
    [InlineData("Workspace")]
    [InlineData("SomeMethod|FullyQualifiedName~Other")]
    [InlineData("SomeMethod,")]
    public void VerificationPipeline_RejectsInvalidScenariosBeforeStages(string scenarios)
    {
        var fixture = InitializeFixture(preflightFails: false);
        var result = RunVerification(fixture, scenarios: scenarios);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(fixture.CommandLogPath));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerificationPipeline_WithInheritedAcceptanceEnvironment_ExcludesScenarioClassFromSolutionTests(bool selectScenario)
    {
        var fixture = InitializeFixture(preflightFails: false);
        var result = RunVerification(fixture, scenarios: selectScenario ? "NativeWorkspaceFolderEdit_UsesFormalDriver" : null, inheritedAcceptance: true);
        Assert.Equal(0, result.ExitCode);
        var commands = File.ReadAllLines(fixture.CommandLogPath);
        var solutionTest = commands[FindSingle(commands, "dotnet test ICP-Platform.sln")];
        Assert.Contains("--filter FullyQualifiedName!~ICP.Platform.Acceptance.Tests.AcceptanceGateTests.", solutionTest);
        Assert.Contains(commands, command => command.TrimEnd() == "inherited-acceptance=1;workspace=1;component=1;presentation=1");
        if (selectScenario) Assert.Contains("acceptance -Scenario NativeWorkspaceFolderEdit_UsesFormalDriver -NoBuild", commands);
        else Assert.DoesNotContain(commands, command => command.StartsWith("acceptance "));
    }
    public void Dispose()
    {
        if (!Directory.Exists(_fixtureRoot))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(_fixtureRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        Directory.Delete(_fixtureRoot, recursive: true);
    }

    private Fixture InitializeFixture(bool preflightFails, bool lintFails = false)
    {
        var scriptsRoot = Path.Combine(_fixtureRoot, "scripts");
        var shimRoot = Path.Combine(_fixtureRoot, "shims");
        Directory.CreateDirectory(scriptsRoot);
        Directory.CreateDirectory(shimRoot);
        var repositoryRoot = FindRepositoryRoot();
        File.Copy(
            Path.Combine(repositoryRoot, "scripts", "verify.ps1"),
            Path.Combine(scriptsRoot, "verify.ps1"));
        File.WriteAllText(
            Path.Combine(scriptsRoot, "check-current-docs.ps1"),
            "$ErrorActionPreference = 'Stop'\r\n");
        File.WriteAllText(
            Path.Combine(scriptsRoot, "validate-dic-localization.ps1"),
            "$ErrorActionPreference = 'Stop'\r\n");
        File.WriteAllText(
            Path.Combine(scriptsRoot, "accept.ps1"),
            "Add-Content -LiteralPath $env:NEWP_FAKE_COMMAND_LOG -Value ('acceptance ' + ($args -join ' '))\r\n");

        var commandLogPath = Path.Combine(_fixtureRoot, "commands.log");
        File.WriteAllText(
            Path.Combine(shimRoot, "dotnet.cmd"),
            "@echo off\r\n" +
            "echo dotnet %*>>\"%NEWP_FAKE_COMMAND_LOG%\"\r\n" +
            "if \"%1\"==\"test\" echo inherited-acceptance=%NEWP_RUN_ACCEPTANCE%;workspace=%NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE%;component=%NEWP_RUN_NATIVE_COMPONENT_ACCEPTANCE%;presentation=%NEWP_ACCEPTANCE_PRESENTATION% >>\"%NEWP_FAKE_COMMAND_LOG%\"\r\n" +
            "echo %*| findstr /C:\"migration-manifest/MigrationManifest.csproj\" >nul\r\n" +
            "if errorlevel 1 exit /b 0\r\n" +
            "if not \"%NEWP_FAKE_PREFLIGHT_FAIL%\"==\"1\" exit /b 0\r\n" +
            $"echo {Fixture.PreflightDiagnostic} 1>&2\r\n" +
            "exit /b 37\r\n");
        File.WriteAllText(
            Path.Combine(shimRoot, "corepack.cmd"),
            "@echo off\r\n" +
            "echo corepack %*>>\"%NEWP_FAKE_COMMAND_LOG%\"\r\n" +
            "if not \"%NEWP_FAKE_LINT_FAIL%\"==\"1\" exit /b 0\r\n" +
            "echo %*| findstr /C:\"frontend lint\" >nul\r\n" +
            "if errorlevel 1 exit /b 0\r\n" +
            "echo fixture.tsx:12 react-hooks/rules-of-hooks 1>&2\r\n" +
            "exit /b 1\r\n");

        RunGit("init");
        RunGit("config", "user.name", "Verification Test");
        RunGit("config", "user.email", "verification@example.invalid");
        RunGit("add", ".");
        RunGit("commit", "-m", "fixture");

        return new Fixture(
            _fixtureRoot,
            commandLogPath,
            Path.Combine(_fixtureRoot, ".codex-temp", "verify", "current-result.json"),
            shimRoot,
            preflightFails,
            lintFails);
    }

    private ProcessResult RunVerification(Fixture fixture, bool quick = true, string? scenarios = "NativeWorkspaceFolderEdit_UsesFormalDriver", bool legacyGate = false, bool inheritedAcceptance = false)
    {
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = fixture.ShimRoot + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["NEWP_FAKE_COMMAND_LOG"] = fixture.CommandLogPath,
            ["NEWP_FAKE_PREFLIGHT_FAIL"] = fixture.PreflightFails ? "1" : "0",
            ["NEWP_FAKE_LINT_FAIL"] = fixture.LintFails ? "1" : "0"
        };
        if (inheritedAcceptance)
        {
            foreach (var name in new[] { "NEWP_RUN_ACCEPTANCE", "NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "NEWP_RUN_NATIVE_COMPONENT_ACCEPTANCE", "NEWP_ACCEPTANCE_PRESENTATION" }) environment[name] = "1";
        }
        List<string> arguments = ["-NoProfile", "-File", Path.Combine(fixture.Root, "scripts", "verify.ps1")];
        if (quick)
        {
            arguments.Add("-Quick");
        }
        if (scenarios is not null) arguments.AddRange([legacyGate ? "-AcceptanceGate" : "-AcceptanceScenario", scenarios]);
        return RunProcess(
            "pwsh",
            arguments,
            environment);
    }

    private void RunGit(params string[] arguments)
    {
        var result = RunProcess("git", arguments);
        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
    }

    private ProcessResult RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = _fixtureRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                process.StartInfo.Environment[name] = value;
            }
        }

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        Assert.True(process.Start(), $"Failed to start {fileName}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static int FindSingle(IReadOnlyList<string> commands, string fragment)
    {
        var matches = commands
            .Select((command, index) => (command, index))
            .Where(item => item.command.Contains(fragment, StringComparison.Ordinal))
            .ToArray();
        Assert.Single(matches);
        return matches[0].index;
    }

    private static int CountOccurrences(string text, string fragment)
    {
        var count = 0;
        for (var index = text.IndexOf(fragment, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "scripts", "verify.ps1")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record Fixture(
        string Root,
        string CommandLogPath,
        string CurrentResultPath,
        string ShimRoot,
        bool PreflightFails,
        bool LintFails)
    {
        internal const string PreflightDiagnostic =
            "MIGRATION_MANIFEST_SOURCE_DRIFT module='Organization' provider='SqlServer' field='contentDigest'";
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
