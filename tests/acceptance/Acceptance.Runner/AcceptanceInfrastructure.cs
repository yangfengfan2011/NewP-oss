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
    /// 参与脱敏与证据扫描的最小 token 长度。两端使用同一下限：
    /// 更短的值（例如 <c>sa</c> 这样的登录名）不是凭据，却会把证据里任何含该子串的正常词
    /// 替换成 <c>[REDACTED]</c> 或误报泄漏。凭据本身（生成密码、连接串）远长于该下限。
    /// </summary>
    public const int MinimumSecretTokenLength = 6;

    /// <summary>
    /// 把已知凭据 token 从任何进入 evidence 的文本中移除。
    /// 必须按长度降序替换：短 token 若先命中，会在更长的 token（例如包含该密码的完整连接串）
    /// 内部留下未被整体替换的残片。
    /// </summary>
    public static string Sanitize(IEnumerable<string> secretTokens, string? value)
    {
        ArgumentNullException.ThrowIfNull(secretTokens);

        var sanitized = value ?? string.Empty;
        foreach (var token in SignificantSecretTokens(secretTokens)
                     .OrderByDescending(static token => token.Length))
        {
            sanitized = sanitized.Replace(token, "[REDACTED]", StringComparison.Ordinal);
        }
        return sanitized;
    }

    /// <summary>
    /// 登记一条连接的相关凭据形态。除裸密码外同时登记 <c>Password=</c> 与 <c>Pwd=</c> 形式，
    /// 使连接串片段在只出现前缀形态时也能被整体替换。
    /// </summary>
    public static void AddConnectionSecrets(
        ICollection<string> secretTokens,
        string connectionString,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(secretTokens);

        secretTokens.Add(connectionString);
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        secretTokens.Add(password);
        secretTokens.Add($"Password={password}");
        secretTokens.Add($"Pwd={password}");
    }

    /// <summary>
    /// 反向扫描本轮全部证据文件，确认没有已知凭据以字节形式落盘。
    /// 与 <see cref="Sanitize"/> 使用同一组有效 token，避免"脱敏跳过但扫描报警"的不一致。
    /// </summary>
    public static async Task<bool> ScanForSecretsAsync(
        string runDirectory,
        IEnumerable<string> secretTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretTokens);

        var patterns = SignificantSecretTokens(secretTokens)
            .Select(Encoding.UTF8.GetBytes)
            .ToArray();
        if (patterns.Length == 0)
        {
            return false;
        }

        foreach (var path in Directory.EnumerateFiles(runDirectory, "*", SearchOption.AllDirectories))
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (patterns.Any(pattern => bytes.AsSpan().IndexOf(pattern) >= 0))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 按名称取属性，大小写不敏感；缺失时抛出指名属性的异常。
    /// 契约断言必须区分"字段可缺失"与"字段必需"：两者混用同一个返回空串的 helper 时，
    /// 缺失字段会退化成值不匹配（例如 <c>returned (, , )</c>），失败信号无法定位到实际原因。
    /// </summary>
    public static JsonElement RequireProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new InvalidOperationException($"JSON property {name} was missing.");
    }

    /// <summary>值用于断言或所有权判定时使用：缺失或为 null 都立即失败。</summary>
    public static string ReadRequiredString(JsonElement element, string name) =>
        RequireProperty(element, name).GetString()
            ?? throw new InvalidOperationException($"JSON property {name} was null.");

    /// <summary>缺失是合法运行态时使用（例如成功时为空的 failureCode）：缺失返回空串。</summary>
    public static string ReadOptionalString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind is JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : string.Empty;
            }
        }

        return string.Empty;
    }

    /// <summary>取必需的对象属性；类型不符与缺失都指名失败。</summary>
    public static JsonElement ReadRequiredObject(JsonElement element, string name)
    {
        var property = RequireProperty(element, name);
        return property.ValueKind is JsonValueKind.Object
            ? property
            : throw new InvalidOperationException($"JSON property {name} was not an object.");
    }

    /// <summary>取必需的整数属性；同时接受 JSON number 与可解析的数字字符串。</summary>
    public static long ReadRequiredInt64(JsonElement element, string name)
    {
        var property = RequireProperty(element, name);
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetInt64(),
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => throw new InvalidOperationException($"JSON property {name} was not an integer.")
        };
    }

    /// <summary>
    /// 递归删除前确认目标仍在本轮 run 的 resources 边界内，且目录树中没有 reparse point。
    /// .NET 的递归删除不跟随 reparse point，因此这里是纵深防御而非唯一屏障：
    /// 目录树里出现链接说明现场已偏离预期，此时保留证据比继续删除更重要。
    /// </summary>
    public static void EnsureOwnedResourceRoot(string runDirectory, string path)
    {
        var resources = Path.GetFullPath(Path.Combine(runDirectory, "resources"))
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(resources, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Acceptance resource root escaped the owned run directory.");
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories)
                .Any(item => (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidOperationException(
                "Acceptance resource root contains a reparse point and will be preserved.");
        }
    }

    private static IEnumerable<string> SignificantSecretTokens(IEnumerable<string> secretTokens) =>
        secretTokens
            .Where(static token =>
                !string.IsNullOrWhiteSpace(token) && token.Length >= MinimumSecretTokenLength)
            .Distinct(StringComparer.Ordinal);
}
