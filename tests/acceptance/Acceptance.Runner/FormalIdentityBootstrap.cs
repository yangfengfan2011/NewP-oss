using System.Text.RegularExpressions;

namespace ICP.Platform.Acceptance;

internal sealed record FormalIdentityBootstrapResult(
    string TemporaryCredential,
    int ProcessId);

/// <summary>
/// 通过正式 Host 离线维护命令初始化本地管理员；验收 runner 只负责真实 console 交互与一次性凭据捕获。
/// </summary>
internal static class FormalIdentityBootstrap
{
    private const int MaximumFailureOutputLength = 8_000;

    public static async Task<FormalIdentityBootstrapResult> RunAsync(
        string repositoryRoot,
        string resourceRoot,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var hostDll = Path.Combine(
            repositoryRoot,
            "apps",
            "host-api",
            "bin",
            "Release",
            "net10.0",
            "HostApi.dll");
        var process = await WindowsInteractiveConsole.RunAsync(
            "dotnet.exe",
            [hostDll, "identity", "bootstrap-local-admin"],
            repositoryRoot,
            ResolveHostWorkingDirectory(repositoryRoot),
            resourceRoot,
            environment,
            cancellationToken);
        var output = Regex.Replace(process.Output, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
        var credentials = Regex.Matches(
                output,
                "Temporary credential:\\s*(?<secret>[^\\s]+)",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["secret"].Value)
            .ToArray();
        if (process.ExitCode != 0 || credentials.Length != 1)
        {
            throw new InvalidOperationException(CreateFailureMessage(process.ExitCode, credentials.Length, output));
        }

        return new FormalIdentityBootstrapResult(credentials[0], process.ProcessId);
    }

    // 与正式 bootstrap 脚本保持一致：维护命令必须从 Host 项目目录加载 appsettings.*.json。
    internal static string ResolveHostWorkingDirectory(string repositoryRoot) =>
        Path.GetFullPath(Path.Combine(repositoryRoot, "apps", "host-api"));

    // 失败诊断必须保留真实 console 尾部，同时在进入验收证据前移除临时凭据和常见连接密码。
    internal static string CreateFailureMessage(int exitCode, int credentialCount, string output)
    {
        var summary =
            $"Formal identity bootstrap failed with exit code {exitCode} and {credentialCount} credential results.";
        var sanitized = Regex.Replace(output, "\u001b\\[[0-?]*[ -/]*[@-~]", string.Empty);
        sanitized = Regex.Replace(
            sanitized,
            "Temporary credential:\\s*[^\\s]+",
            "Temporary credential: [REDACTED]",
            RegexOptions.CultureInvariant);
        // 连接串的值可以带引号，未加引号时也可以含空格，只在 `;` 或行尾结束。
        // 旧写法 [^;\s]+ 在第一个空格处停下，会把 Password='a b' 的后半截留在诊断里。
        sanitized = Regex.Replace(
            sanitized,
            "(?<name>Password|Pwd)\\s*=\\s*(?:\"[^\"\\r\\n]*\"|'[^'\\r\\n]*'|[^;\\r\\n]*)",
            "${name}=[REDACTED]",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        sanitized = sanitized.Trim();
        if (sanitized.Length == 0)
        {
            return summary;
        }

        if (sanitized.Length > MaximumFailureOutputLength)
        {
            sanitized = sanitized[^MaximumFailureOutputLength..];
        }

        return $"{summary}{Environment.NewLine}Console output tail:{Environment.NewLine}{sanitized}";
    }
}
