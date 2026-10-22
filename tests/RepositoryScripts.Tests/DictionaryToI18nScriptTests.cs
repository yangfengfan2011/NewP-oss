using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public class DictionaryToI18nScriptTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void DictionaryToI18nScript_WritesEnabledLanguageFiles_AndAllowsLaterInputsToOverride()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var outputRoot = Path.Combine(tempRoot, "resources", "localization");
        var configPath = Path.Combine(outputRoot, "languages.json");
        Directory.CreateDirectory(outputRoot);

        File.WriteAllText(
            configPath,
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

        var firstDictionaryPath = Path.Combine(tempRoot, "Common.dic");
        var secondDictionaryPath = Path.Combine(tempRoot, "PlatformOverlay.dic");

        File.WriteAllText(
            firstDictionaryPath,
            """
            <DictionarySerializer>
              <Context Name="Common" IsWeb="true">
                <Label Name="OK">
                  <LocalizedLabel LanguageRef="zhs">确定</LocalizedLabel>
                  <LocalizedLabel LanguageRef="en">OK</LocalizedLabel>
                  <LocalizedLabel LanguageRef="fr">D'accord</LocalizedLabel>
                </Label>
              </Context>
            </DictionarySerializer>
            """,
            Encoding.UTF8);

        File.WriteAllText(
            secondDictionaryPath,
            """
            <DictionarySerializer>
              <Context Name="Common" IsWeb="true">
                <Label Name="OK">
                  <LocalizedLabel LanguageRef="zhs">确认</LocalizedLabel>
                  <LocalizedLabel LanguageRef="en">Confirm</LocalizedLabel>
                </Label>
              </Context>
              <Context Name="Platform" IsWeb="true">
                <Label Name="Prompt">
                  <LocalizedLabel LanguageRef="zhs">提示框</LocalizedLabel>
                  <LocalizedLabel LanguageRef="en">Prompt box</LocalizedLabel>
                </Label>
              </Context>
            </DictionarySerializer>
            """,
            Encoding.UTF8);

        var scriptPath = Path.Combine(repoRoot, "scripts", "migration", "dictionary-to-i18n.ps1");
        var result = RunPowerShellCommand(
            repoRoot,
            $$"""
            & '{{QuoteForPowerShell(scriptPath)}}' -InputPath @('{{QuoteForPowerShell(firstDictionaryPath)}}','{{QuoteForPowerShell(secondDictionaryPath)}}') -OutputRoot '{{QuoteForPowerShell(outputRoot)}}' -LanguagesConfigPath '{{QuoteForPowerShell(configPath)}}'
            """);

        var zhsCommonPath = Path.Combine(outputRoot, "zhs", "dictionary", "Common.json");
        var enCommonPath = Path.Combine(outputRoot, "en", "dictionary", "Common.json");
        var zhsPlatformPath = Path.Combine(outputRoot, "zhs", "dictionary", "Platform.json");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(zhsCommonPath), $"Expected dictionary output at {zhsCommonPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(enCommonPath), $"Expected dictionary output at {enCommonPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(zhsPlatformPath), $"Expected dictionary output at {zhsPlatformPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.False(Directory.Exists(Path.Combine(outputRoot, "fr")), "Unsupported language directory should not be created.");

        using var zhsCommonDocument = JsonDocument.Parse(File.ReadAllText(zhsCommonPath, Encoding.UTF8));
        using var enCommonDocument = JsonDocument.Parse(File.ReadAllText(enCommonPath, Encoding.UTF8));
        using var zhsPlatformDocument = JsonDocument.Parse(File.ReadAllText(zhsPlatformPath, Encoding.UTF8));

        Assert.Equal("确认", zhsCommonDocument.RootElement.GetProperty("Common.OK").GetString());
        Assert.Equal("Confirm", enCommonDocument.RootElement.GetProperty("Common.OK").GetString());
        Assert.Equal("提示框", zhsPlatformDocument.RootElement.GetProperty("Platform.Prompt").GetString());

        var combinedOutput = $"{result.StdOut}\n{result.StdErr}";
        Assert.Contains("fr", combinedOutput, StringComparison.OrdinalIgnoreCase);
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
        var tempRoot = Path.Combine(repoRoot, "artifacts", "test-temp", $"dictionary-i18n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        _tempRoots.Add(tempRoot);
        return tempRoot;
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

    private static ScriptResult RunPowerShellCommand(string workingDirectory, string command)
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
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);

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

        Assert.True(process.Start(), "Failed to start PowerShell command.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string QuoteForPowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private sealed record ScriptResult(int ExitCode, string StdOut, string StdErr);
}
