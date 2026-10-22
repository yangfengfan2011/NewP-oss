using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public sealed class VerificationCandidateSnapshotTests : IDisposable
{
    private readonly string _fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        $"newp-verification-candidate-{Guid.NewGuid():N}");

    [Fact]
    public void WorkingTreeSnapshot_ChangesWhenAlreadyDirtyFileContentChanges()
    {
        var trackedPath = InitializeCommittedRepository();
        File.WriteAllText(trackedPath, "first dirty content");
        var objectDatabaseBefore = RunGit("count-objects", "-v").StdOut;

        var repositoryRoot = FindRepositoryRoot();
        var verificationScriptPath = Path.Combine(repositoryRoot, "scripts", "verify.ps1");
        var probeScript = $$"""
            $repoRoot = '{{EscapePowerShellLiteral(_fixtureRoot)}}'
            $runDirectory = Join-Path $repoRoot '.candidate-probe'
            New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                '{{EscapePowerShellLiteral(verificationScriptPath)}}',
                [ref]$tokens,
                [ref]$errors)
            if ($errors.Count -gt 0) { throw ($errors | Out-String) }
            $functions = $ast.FindAll(
                {
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        ($node.Name -eq 'Test-GitObjectId' -or $node.Name -eq 'Get-WorkingTreeSnapshot')
                },
                $true)
            if ($functions.Count -lt 2) { throw 'Git object validation and snapshot functions were expected.' }
            foreach ($function in $functions) { Invoke-Expression $function.Extent.Text }
            $before = Get-WorkingTreeSnapshot
            [System.IO.File]::WriteAllText((Join-Path $repoRoot 'tracked.txt'), 'second dirty content')
            $after = Get-WorkingTreeSnapshot
            [pscustomobject]@{
                beforeDirty = $before.Dirty
                afterDirty = $after.Dirty
                beforeEntryCount = $before.EntryCount
                afterEntryCount = $after.EntryCount
                beforeDigest = $before.Digest
                afterDigest = $after.Digest
            } | ConvertTo-Json -Compress
            """;

        var result = RunProcess("pwsh", ["-NoProfile", "-Command", probeScript]);

        Assert.True(
            result.ExitCode == 0,
            $"Snapshot probe failed.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        using var document = JsonDocument.Parse(result.StdOut.Trim());
        var snapshot = document.RootElement;
        Assert.True(snapshot.GetProperty("beforeDirty").GetBoolean());
        Assert.True(snapshot.GetProperty("afterDirty").GetBoolean());
        Assert.Equal(1, snapshot.GetProperty("beforeEntryCount").GetInt32());
        Assert.Equal(1, snapshot.GetProperty("afterEntryCount").GetInt32());
        Assert.NotEqual(
            snapshot.GetProperty("beforeDigest").GetString(),
            snapshot.GetProperty("afterDigest").GetString());
        RunGit("diff", "--cached", "--quiet");
        Assert.Equal(objectDatabaseBefore, RunGit("count-objects", "-v").StdOut);
    }

    [Fact]
    public void CandidateProbes_WhenSuccessfulGitWritesStderr_UseOnlyStdoutObjectIds()
    {
        InitializeCommittedRepository();
        var expectedHead = RunGit("rev-parse", "HEAD").StdOut.Trim();
        var expectedTree = RunGit("rev-parse", "HEAD^{tree}").StdOut.Trim();

        var repositoryRoot = FindRepositoryRoot();
        var verificationScriptPath = Path.Combine(repositoryRoot, "scripts", "verify.ps1");
        var probeScript = $$"""
            $repoRoot = '{{EscapePowerShellLiteral(_fixtureRoot)}}'
            $runDirectory = Join-Path $repoRoot '.candidate-probe'
            New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                '{{EscapePowerShellLiteral(verificationScriptPath)}}',
                [ref]$tokens,
                [ref]$errors)
            if ($errors.Count -gt 0) { throw ($errors | Out-String) }
            $functions = $ast.FindAll(
                {
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        ($node.Name -eq 'Test-GitObjectId' -or
                         $node.Name -like 'Get-WorkingTreeSnapshot*' -or
                         $node.Name -eq 'Get-HeadCommitOrUnavailable')
                },
                $true)
            foreach ($function in $functions) { Invoke-Expression $function.Extent.Text }
            $env:GIT_TRACE2 = '1'
            $snapshot = Get-WorkingTreeSnapshotOrUnavailable
            $head = Get-HeadCommitOrUnavailable
            [pscustomobject]@{
                dirty = $snapshot.Dirty
                entryCount = $snapshot.EntryCount
                digest = $snapshot.Digest
                commit = $head.Commit
                snapshotDiagnostic = $snapshot.Diagnostic
                headDiagnostic = $head.Diagnostic
            } | ConvertTo-Json -Compress
            """;

        var result = RunProcess("pwsh", ["-NoProfile", "-Command", probeScript]);

        Assert.True(
            result.ExitCode == 0,
            $"Candidate probe failed.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        using var document = JsonDocument.Parse(result.StdOut.Trim());
        var candidate = document.RootElement;
        Assert.False(candidate.GetProperty("dirty").GetBoolean());
        Assert.Equal(0, candidate.GetProperty("entryCount").GetInt32());
        Assert.Equal(expectedTree, candidate.GetProperty("digest").GetString());
        Assert.Equal(expectedHead, candidate.GetProperty("commit").GetString());
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("snapshotDiagnostic").ValueKind);
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("headDiagnostic").ValueKind);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("read-tree")]
    [InlineData("add")]
    [InlineData("write-tree")]
    public void WorkingTreeSnapshot_WhenGitCommandFails_DegradesWithSanitizedDiagnostic(string failedOperation)
    {
        Directory.CreateDirectory(_fixtureRoot);

        var repositoryRoot = FindRepositoryRoot();
        var verificationScriptPath = Path.Combine(repositoryRoot, "scripts", "verify.ps1");
        var probeScript = $$"""
            $repoRoot = '{{EscapePowerShellLiteral(_fixtureRoot)}}'
            $runDirectory = Join-Path $repoRoot '.candidate-probe'
            New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                '{{EscapePowerShellLiteral(verificationScriptPath)}}',
                [ref]$tokens,
                [ref]$errors)
            if ($errors.Count -gt 0) { throw ($errors | Out-String) }
            $functions = $ast.FindAll(
                {
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        ($node.Name -eq 'Test-GitObjectId' -or $node.Name -like 'Get-WorkingTreeSnapshot*')
                },
                $true)
            if ($functions.Count -lt 3) { throw 'Git object validation and both snapshot functions were expected.' }
            foreach ($function in $functions) { Invoke-Expression $function.Extent.Text }
            function git {
                if ($args -contains '{{failedOperation}}') {
                    $global:LASTEXITCODE = 23
                    'TOP-SECRET-GIT-OUTPUT'
                    return
                }
                if ($args -contains 'status') {
                    $global:LASTEXITCODE = 0
                    return
                }
                if ($args -contains 'rev-parse') {
                    $global:LASTEXITCODE = 0
                    Join-Path $repoRoot '.git/objects'
                    return
                }
                if ($args -contains 'write-tree') {
                    $global:LASTEXITCODE = 0
                    '1111111111111111111111111111111111111111'
                    return
                }
                $global:LASTEXITCODE = 0
            }
            $snapshot = Get-WorkingTreeSnapshotOrUnavailable
            [pscustomobject]@{
                digestIsNull = $null -eq $snapshot.Digest
                diagnostic = $snapshot.Diagnostic
            } | ConvertTo-Json -Compress
            """;

        var result = RunProcess("pwsh", ["-NoProfile", "-Command", probeScript]);

        Assert.True(
            result.ExitCode == 0,
            $"Snapshot probe threw instead of degrading.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        using var document = JsonDocument.Parse(result.StdOut.Trim());
        var snapshot = document.RootElement;
        Assert.True(snapshot.GetProperty("digestIsNull").GetBoolean());
        var diagnostic = snapshot.GetProperty("diagnostic").GetString();
        Assert.Contains($"git {failedOperation} exited with code 23", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP-SECRET-GIT-OUTPUT", diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("tree")]
    public void CandidateProbe_WhenGitReturnsInvalidObjectId_DegradesThatProbe(string invalidProbe)
    {
        Directory.CreateDirectory(_fixtureRoot);

        var repositoryRoot = FindRepositoryRoot();
        var verificationScriptPath = Path.Combine(repositoryRoot, "scripts", "verify.ps1");
        var probeScript = $$"""
            $repoRoot = '{{EscapePowerShellLiteral(_fixtureRoot)}}'
            $runDirectory = Join-Path $repoRoot '.candidate-probe'
            New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                '{{EscapePowerShellLiteral(verificationScriptPath)}}',
                [ref]$tokens,
                [ref]$errors)
            if ($errors.Count -gt 0) { throw ($errors | Out-String) }
            $functions = $ast.FindAll(
                {
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        ($node.Name -eq 'Test-GitObjectId' -or
                         $node.Name -like 'Get-WorkingTreeSnapshot*' -or
                         $node.Name -eq 'Get-HeadCommitOrUnavailable')
                },
                $true)
            foreach ($function in $functions) { Invoke-Expression $function.Extent.Text }
            function git {
                $global:LASTEXITCODE = 0
                if ($args -contains 'status' -or $args -contains 'read-tree' -or $args -contains 'add') { return }
                if ($args -contains '--git-path') {
                    Join-Path $repoRoot '.git/objects'
                    return
                }
                if ($args -contains 'write-tree') {
                    if ('{{invalidProbe}}' -eq 'tree') { 'not-a-git-object-id' } else { '1111111111111111111111111111111111111111' }
                    return
                }
                if ($args -contains 'rev-parse') {
                    if ('{{invalidProbe}}' -eq 'head') { 'not-a-git-object-id' } else { '2222222222222222222222222222222222222222' }
                }
            }
            $snapshot = Get-WorkingTreeSnapshotOrUnavailable
            $head = Get-HeadCommitOrUnavailable
            [pscustomobject]@{
                digest = $snapshot.Digest
                snapshotDiagnostic = $snapshot.Diagnostic
                commit = $head.Commit
                headDiagnostic = $head.Diagnostic
            } | ConvertTo-Json -Compress
            """;

        var result = RunProcess("pwsh", ["-NoProfile", "-Command", probeScript]);

        Assert.True(
            result.ExitCode == 0,
            $"Candidate probe threw instead of degrading.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        using var document = JsonDocument.Parse(result.StdOut.Trim());
        var candidate = document.RootElement;
        if (invalidProbe == "head")
        {
            Assert.Equal(JsonValueKind.Null, candidate.GetProperty("commit").ValueKind);
            Assert.Contains("git rev-parse returned an invalid object id", candidate.GetProperty("headDiagnostic").GetString());
            Assert.Equal("1111111111111111111111111111111111111111", candidate.GetProperty("digest").GetString());
            Assert.Equal(JsonValueKind.Null, candidate.GetProperty("snapshotDiagnostic").ValueKind);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, candidate.GetProperty("digest").ValueKind);
            Assert.Contains("git write-tree returned an invalid object id", candidate.GetProperty("snapshotDiagnostic").GetString());
            Assert.Equal("2222222222222222222222222222222222222222", candidate.GetProperty("commit").GetString());
            Assert.Equal(JsonValueKind.Null, candidate.GetProperty("headDiagnostic").ValueKind);
        }
    }

    // 归属机制自己出问题时必须降级成「这次无法归属」，不能把整轮验证一起带走：
    // 快照跑在写出第一份 result.json 之前，抛出去连一条失败记录都不会留下。
    [Fact]
    public void VerificationResult_WhenRepositoryIsUnavailable_IsWrittenWithUnknownCandidateState()
    {
        Directory.CreateDirectory(_fixtureRoot);

        var repositoryRoot = FindRepositoryRoot();
        var verificationScriptPath = Path.Combine(repositoryRoot, "scripts", "verify.ps1");
        var probeScript = $$"""
            $repoRoot = '{{EscapePowerShellLiteral(_fixtureRoot)}}'
            $runId = 'candidate-unavailable'
            $runDirectory = Join-Path $repoRoot '.candidate-probe'
            $resultPath = Join-Path $runDirectory 'result.json'
            $currentResultPath = Join-Path $repoRoot 'current-result.json'
            $msBuildDiagnosticDirectory = Join-Path $runDirectory 'msbuild'
            New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
            New-Item -ItemType Directory -Path $msBuildDiagnosticDirectory -Force | Out-Null
            $startedAtUtc = [DateTimeOffset]::UtcNow
            $stageResults = [System.Collections.Generic.List[object]]::new()
            $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
            $Quick = [System.Management.Automation.SwitchParameter]::new($false)
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                '{{EscapePowerShellLiteral(verificationScriptPath)}}',
                [ref]$tokens,
                [ref]$errors)
            if ($errors.Count -gt 0) { throw ($errors | Out-String) }
            $functions = $ast.FindAll(
                {
                    param($node)
                    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                        ($node.Name -eq 'Test-GitObjectId' -or
                         $node.Name -like 'Get-WorkingTreeSnapshot*' -or
                         $node.Name -eq 'Get-HeadCommitOrUnavailable' -or
                         $node.Name -eq 'Save-VerificationResult')
                },
                $true)
            foreach ($function in $functions) { Invoke-Expression $function.Extent.Text }
            function git {
                $global:LASTEXITCODE = 17
                'TOP-SECRET-PROBE-OUTPUT'
            }
            $startSnapshot = Get-WorkingTreeSnapshotOrUnavailable
            $startHead = Get-HeadCommitOrUnavailable
            Save-VerificationResult -Status 'passed'
            Get-Content -LiteralPath $resultPath -Raw
            """;

        var result = RunProcess("pwsh", ["-NoProfile", "-Command", probeScript]);

        Assert.True(
            result.ExitCode == 0,
            $"Verification result was not written.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        using var document = JsonDocument.Parse(result.StdOut.Trim());
        var root = document.RootElement;
        Assert.Equal("passed", root.GetProperty("status").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("headCommit").ValueKind);
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("dirty").ValueKind);
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("dirtyEntryCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("treeChangedDuringRun").ValueKind);
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("headChangedDuringRun").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(candidate.GetProperty("snapshotDiagnostic").GetString()));
        Assert.DoesNotContain("TOP-SECRET-PROBE-OUTPUT", result.StdOut, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_fixtureRoot, "current-result.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureRoot))
        {
            foreach (var filePath in Directory.EnumerateFiles(_fixtureRoot, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }
            Directory.Delete(_fixtureRoot, recursive: true);
        }
    }

    private ProcessResult RunGit(params string[] arguments)
    {
        var result = RunProcess("git", arguments);
        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        return result;
    }

    private string InitializeCommittedRepository()
    {
        Directory.CreateDirectory(_fixtureRoot);
        RunGit("init");
        RunGit("config", "user.name", "Verification Test");
        RunGit("config", "user.email", "verification@example.invalid");
        File.AppendAllText(Path.Combine(_fixtureRoot, ".git", "info", "exclude"), ".candidate-probe/\n");

        var trackedPath = Path.Combine(_fixtureRoot, "tracked.txt");
        File.WriteAllText(trackedPath, "baseline");
        RunGit("add", "tracked.txt");
        RunGit("commit", "-m", "baseline");
        return trackedPath;
    }

    private ProcessResult RunProcess(string fileName, IReadOnlyList<string> arguments)
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

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
