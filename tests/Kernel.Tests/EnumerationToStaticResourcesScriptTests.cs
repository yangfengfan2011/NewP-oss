using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class EnumerationToStaticResourcesScriptTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void EnumerationToStaticResourcesScript_WritesStructureAndLocalization_AndWarnsOnMismatches()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var outputRoot = Path.Combine(tempRoot, "resources");
        var localizationRoot = Path.Combine(outputRoot, "localization");
        var enumerationsRoot = Path.Combine(outputRoot, "enumerations");
        var configPath = Path.Combine(localizationRoot, "languages.json");
        Directory.CreateDirectory(localizationRoot);
        Directory.CreateDirectory(enumerationsRoot);

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

        var enumerationPath = Path.Combine(tempRoot, "Enumerations.enum");
        var dictionaryPath = Path.Combine(tempRoot, "Enumerations.dic");

        File.WriteAllText(
            enumerationPath,
            """
            <root>
              <Enumeration Name="Gender">
                <EnumerationItem Name="Female" />
                <EnumerationItem Name="Male" />
              </Enumeration>
              <Enumeration Name="MessageState">
                <EnumerationItem Name="Read" />
                <EnumerationItem Name="Unread" />
              </Enumeration>
            </root>
            """,
            Encoding.UTF8);

        File.WriteAllText(
            dictionaryPath,
            """
            <DictionarySerializer>
              <Context Name="Enumerations" IsWeb="true">
                <Label Name="GenderFemale">
                  <LocalizedLabel LanguageRef="zhs">女</LocalizedLabel>
                  <LocalizedLabel LanguageRef="en">Female</LocalizedLabel>
                  <LocalizedLabel LanguageRef="fr">Femme</LocalizedLabel>
                </Label>
                <Label Name="GenderMale">
                  <LocalizedLabel LanguageRef="zhs">男</LocalizedLabel>
                  <LocalizedLabel LanguageRef="en">Male</LocalizedLabel>
                </Label>
                <Label Name="MessageStateRead">
                  <LocalizedLabel LanguageRef="zhs">已读</LocalizedLabel>
                  <LocalizedLabel LanguageRef="en">Read</LocalizedLabel>
                </Label>
                <Label Name="HomePageModeClassicHomePage">
                  <LocalizedLabel LanguageRef="zhs">经典模式</LocalizedLabel>
                  <LocalizedLabel LanguageRef="en">Classic mode</LocalizedLabel>
                </Label>
              </Context>
            </DictionarySerializer>
            """,
            Encoding.UTF8);

        var scriptPath = Path.Combine(repoRoot, "scripts", "migration", "enumeration-to-static-resources.ps1");
        var result = RunPowerShellCommand(
            repoRoot,
            $$"""
            & '{{QuoteForPowerShell(scriptPath)}}' -EnumerationPath '{{QuoteForPowerShell(enumerationPath)}}' -DictionaryPath '{{QuoteForPowerShell(dictionaryPath)}}' -OutputRoot '{{QuoteForPowerShell(outputRoot)}}' -LanguagesConfigPath '{{QuoteForPowerShell(configPath)}}'
            """);

        var genderPath = Path.Combine(enumerationsRoot, "Gender.json");
        var messageStatePath = Path.Combine(enumerationsRoot, "MessageState.json");
        var zhsGenderPath = Path.Combine(localizationRoot, "zhs", "enumerations", "Gender.json");
        var enGenderPath = Path.Combine(localizationRoot, "en", "enumerations", "Gender.json");
        var zhsMessageStatePath = Path.Combine(localizationRoot, "zhs", "enumerations", "MessageState.json");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(genderPath), $"Expected enumeration structure output at {genderPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(messageStatePath), $"Expected enumeration structure output at {messageStatePath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(zhsGenderPath), $"Expected localization output at {zhsGenderPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(enGenderPath), $"Expected localization output at {enGenderPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(zhsMessageStatePath), $"Expected localization output at {zhsMessageStatePath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.False(Directory.Exists(Path.Combine(localizationRoot, "fr")), "Unsupported language directory should not be created.");

        using var genderDocument = JsonDocument.Parse(File.ReadAllText(genderPath, Encoding.UTF8));
        using var messageStateDocument = JsonDocument.Parse(File.ReadAllText(messageStatePath, Encoding.UTF8));
        using var zhsGenderDocument = JsonDocument.Parse(File.ReadAllText(zhsGenderPath, Encoding.UTF8));
        using var enGenderDocument = JsonDocument.Parse(File.ReadAllText(enGenderPath, Encoding.UTF8));
        using var zhsMessageStateDocument = JsonDocument.Parse(File.ReadAllText(zhsMessageStatePath, Encoding.UTF8));

        Assert.Equal("Gender", genderDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal(2, genderDocument.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal("Female", genderDocument.RootElement.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal("Male", genderDocument.RootElement.GetProperty("items")[1].GetProperty("name").GetString());

        Assert.Equal("MessageState", messageStateDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal(2, messageStateDocument.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal("女", zhsGenderDocument.RootElement.GetProperty("Gender.Female").GetString());
        Assert.Equal("Male", enGenderDocument.RootElement.GetProperty("Gender.Male").GetString());
        Assert.Equal("已读", zhsMessageStateDocument.RootElement.GetProperty("MessageState.Read").GetString());
        Assert.False(zhsMessageStateDocument.RootElement.TryGetProperty("MessageState.Unread", out _));

        var combinedOutput = $"{result.StdOut}\n{result.StdErr}";
        Assert.Contains("fr", combinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HomePageModeClassicHomePage", combinedOutput, StringComparison.Ordinal);
        Assert.Contains("MessageState.Unread", combinedOutput, StringComparison.Ordinal);
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
        var tempRoot = Path.Combine(repoRoot, "artifacts", "test-temp", $"enumeration-static-{Guid.NewGuid():N}");
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
                FileName = "powershell.exe",
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
