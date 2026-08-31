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
        var output = await RunGitRawAsync(repositoryRoot, arguments, cancellationToken);
        return output.Trim();
    }

    public static async Task<string> RunGitRawAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var result = await RunProcessAsync("git", arguments, repositoryRoot, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments[0]} 失败：{result.StandardError.Trim()}");
        }

        return result.StandardOutput;
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

    /// <summary>
    /// 把已知凭据 token 从任何进入 evidence 的文本中移除。
    /// 必须按长度降序替换：短 token 若先命中，会在更长的 token（例如包含该密码的完整连接串）
    /// 内部留下未被整体替换的残片。空白 token 一律跳过，否则会把正常文本替换成 [REDACTED]。
    /// </summary>
    public static string Sanitize(IEnumerable<string> secretTokens, string? value)
    {
        ArgumentNullException.ThrowIfNull(secretTokens);

        var sanitized = value ?? string.Empty;
        foreach (var token in secretTokens
                     .Where(static token => !string.IsNullOrWhiteSpace(token))
                     .OrderByDescending(static token => token.Length))
        {
            sanitized = sanitized.Replace(token, "[REDACTED]", StringComparison.Ordinal);
        }
        return sanitized;
    }
}
