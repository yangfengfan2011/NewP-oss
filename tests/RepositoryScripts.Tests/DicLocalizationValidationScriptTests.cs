using System.Diagnostics;
using System.Text;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public sealed class DicLocalizationValidationScriptTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void ValidateDicLocalization_FailsWhenEntryKeyContainsDot()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        WriteLanguages(tempRoot);
        WriteDictionary(
            tempRoot,
            "Organization",
            "PersonListView.dic.json",
            """
            {
              "schemaVersion": "1.0",
              "type": "dic",
              "PersonListView": {
                "Query.Keyword": {
                  "zhs": "关键字",
                  "en": "Keyword"
                }
              }
            }
            """);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "validate-dic-localization.ps1"),
            ["-Root", tempRoot]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Query.Keyword", result.StdErr);
        Assert.Contains("must not contain '.'", result.StdErr);
    }

    [Fact]
    public void ValidateDicLocalization_FailsWhenSupportedLanguageIsMissing()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        WriteLanguages(tempRoot);
        WriteDictionary(
            tempRoot,
            "Organization",
            "PersonListView.dic.json",
            """
            {
              "schemaVersion": "1.0",
              "type": "dic",
              "PersonListView": {
                "Query_Keyword": {
                  "zhs": "关键字"
                }
              }
            }
            """);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "validate-dic-localization.ps1"),
            ["-Root", tempRoot]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Query_Keyword", result.StdErr);
        Assert.Contains("missing supported language 'en'", result.StdErr);
    }

    [Fact]
    public void ValidateDicLocalization_PassesForCurrentRepositoryDictionaries()
    {
        var repoRoot = FindRepositoryRoot();

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "validate-dic-localization.ps1"),
            ["-Root", repoRoot]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("dic localization validation passed", result.StdOut);
    }

    public void Dispose()
    {
        foreach (var tempRoot in _tempRoots)
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private string CreateTempRoot(string repoRoot)
    {
        var tempRoot = Path.Combine(repoRoot, "artifacts", "test-temp", $"dic-localization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        _tempRoots.Add(tempRoot);
        return tempRoot;
    }

    private static void WriteLanguages(string root)
    {
        var platformMetadataRoot = Path.Combine(Path.Combine(root, "frontend", "modules", "platform"), "metadata");
        Directory.CreateDirectory(platformMetadataRoot);
        File.WriteAllText(
            Path.Combine(platformMetadataRoot, "languages.json"),
            """
            {
              "defaultLanguage": "zhs",
              "supportedLanguages": [
                { "code": "zhs", "displayName": "简体中文" },
                { "code": "en", "displayName": "English" }
              ]
            }
            """,
            Encoding.UTF8);
    }

    private static void WriteDictionary(string root, string moduleId, string fileName, string content)
    {
        var dictionaryRoot = Path.Combine(Path.Combine(root, "frontend", "modules", System.Text.RegularExpressions.Regex.Replace(moduleId, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant()), "metadata", "dictionaries");
        Directory.CreateDirectory(dictionaryRoot);
        File.WriteAllText(Path.Combine(dictionaryRoot, fileName), content, Encoding.UTF8);
    }

    private static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static ScriptResult RunPowerShellScript(string workingDirectory, string scriptPath, IReadOnlyList<string> arguments)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("& { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); $target = $args[0]; $targetArgs = @($args | Select-Object -Skip 1); & $target @targetArgs }");
        process.StartInfo.ArgumentList.Add(scriptPath);
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

        Assert.True(process.Start(), $"Failed to start script {scriptPath}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record ScriptResult(int ExitCode, string StdOut, string StdErr);
}
