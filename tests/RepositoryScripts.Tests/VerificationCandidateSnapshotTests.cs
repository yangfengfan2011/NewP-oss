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
        Directory.CreateDirectory(_fixtureRoot);
        RunGit("init");
        RunGit("config", "user.name", "Verification Test");
        RunGit("config", "user.email", "verification@example.invalid");

        var trackedPath = Path.Combine(_fixtureRoot, "tracked.txt");
        File.WriteAllText(trackedPath, "baseline");
        RunGit("add", "tracked.txt");
        RunGit("commit", "-m", "baseline");
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
            $function = $ast.Find(
                { param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-WorkingTreeSnapshot' },
                $true)
            if ($null -eq $function) { throw 'Get-WorkingTreeSnapshot was not found.' }
            Invoke-Expression $function.Extent.Text
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

    // 归属机制自己出问题时必须降级成「这次无法归属」，不能把整轮验证一起带走：
    // 快照跑在写出第一份 result.json 之前，抛出去连一条失败记录都不会留下。
    [Fact]
    public void WorkingTreeSnapshot_WhenRepositoryIsUnavailable_DegradesInsteadOfThrowing()
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
                { param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -like 'Get-WorkingTreeSnapshot*' },
                $true)
            if ($functions.Count -lt 2) { throw 'Both snapshot functions were expected.' }
            foreach ($function in $functions) { Invoke-Expression $function.Extent.Text }
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
        Assert.True(document.RootElement.GetProperty("digestIsNull").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("diagnostic").GetString()));
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
