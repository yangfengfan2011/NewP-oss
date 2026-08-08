using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ICP.Platform.Acceptance;

internal sealed record AcceptanceProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class AcceptanceInfrastructure
{
    public static async Task<AcceptanceProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool redirectOutput = true,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null)
                {
                    startInfo.Environment.Remove(pair.Key);
                }
                else
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动 {fileName}。");
        var outputTask = redirectOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);
        var errorTask = redirectOutput
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);
        await process.WaitForExitAsync(cancellationToken);
        return new AcceptanceProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    public static async Task<string> RunGitAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await RunProcessAsync("git", arguments, repositoryRoot, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments[0]} 失败：{result.StandardError.Trim()}");
        }

        return result.StandardOutput.Trim();
    }

    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("JSON 文件必须具有父目录。");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, AcceptanceJson.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    public static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await WriteJsonAsync(temporaryPath, value, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string RelativeArtifact(string runDirectory, string path) =>
        Path.GetRelativePath(runDirectory, path).Replace('\\', '/');
}
