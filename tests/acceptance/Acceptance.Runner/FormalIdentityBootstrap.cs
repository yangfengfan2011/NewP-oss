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
            throw new InvalidOperationException(
                $"Formal identity bootstrap failed with exit code {process.ExitCode} and {credentials.Length} credential results.");
        }

        return new FormalIdentityBootstrapResult(credentials[0], process.ProcessId);
    }
}
