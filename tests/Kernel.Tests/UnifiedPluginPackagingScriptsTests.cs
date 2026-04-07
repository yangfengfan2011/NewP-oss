using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class UnifiedPluginPackagingScriptsTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void PackUnifiedPlugin_WritesFrontendPublicBasePathMatchingHostStaticAssetsRoute()
    {
        var repoRoot = FindRepositoryRoot();
        var outputRoot = Path.Combine(repoRoot, "artifacts", "plugin-packages", $"script-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        _tempRoots.Add(outputRoot);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "pack-unified-plugin.ps1"),
            [
                "-ModuleName", "Audit",
                "-Configuration", "Debug",
                "-OutputRoot", outputRoot,
                "-Force"
            ]);

        var packageRoot = Path.Combine(outputRoot, "Audit");
        var frontendManifestPath = Path.Combine(packageRoot, "frontend", "plugin.frontend.json");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(frontendManifestPath), $"Expected frontend manifest at {frontendManifestPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");

        using var document = JsonDocument.Parse(File.ReadAllText(frontendManifestPath));
        Assert.Equal(
            "/plugin-assets/Audit/1.0.0/",
            document.RootElement.GetProperty("publicBasePath").GetString());
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "scripts", "pack-unified-plugin.ps1")))
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
        process.StartInfo.ArgumentList.Add("-File");
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
