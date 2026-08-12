using System.Reflection;

namespace ICP.Platform.Kernel.ModuleLoading;

/// <summary>
/// 受控程序集装载使用的精确身份与共享依赖策略。模块主体必须来自计划签发映像；
/// Default ALC 只提供 Host、框架与平台稳定契约，不作为任意同名程序集的探测目录。
/// </summary>
internal static class VerifiedAssemblyIdentityPolicy
{
    private static readonly IReadOnlySet<string> HostPlatformSharedAssemblies = new HashSet<string>(
        [
            "Kernel",
            "Sdk",
            "PluginPlatform",
            "PlatformAuthorization",
            "PlatformCrud",
            "PlatformModeling",
            "PlatformPersistence",
            "PlatformPersistence.Composition",
            "PlatformPersistence.EfCore",
            "PlatformQuery",
            "Microsoft.Data.SqlClient",
            "ICP.Platform.Plugin.Contracts",
            "Identity.Contracts",
            "Authentication.Contracts",
            "Authorization.Contracts"
        ],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> FrameworkPublicKeyTokens = new HashSet<string>(
        ["b03f5f7f11d50a3a", "adb9793829ddae60", "cc7b13ffcd2ddd51", "31bf3856ad364e35"],
        StringComparer.Ordinal);

    internal static bool MatchesExact(AssemblyName actual, AssemblyName expected) =>
        string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase)
        && Equals(actual.Version, expected.Version)
        && string.Equals(NormalizeCulture(actual.CultureName), NormalizeCulture(expected.CultureName), StringComparison.OrdinalIgnoreCase)
        && TokensEqual(actual.GetPublicKeyToken(), expected.GetPublicKeyToken())
        && ProcessorArchitectureMatches(actual, expected)
        && actual.ContentType == expected.ContentType
        && (actual.Flags & AssemblyNameFlags.Retargetable) == (expected.Flags & AssemblyNameFlags.Retargetable);

    internal static bool IsHostPlatformSharedDependency(AssemblyName requested)
    {
        var name = requested.Name;
        return !string.IsNullOrWhiteSpace(name)
            && (HostPlatformSharedAssemblies.Contains(name)
                || IsFrameworkAssembly(requested, name));
    }

    private static string NormalizeCulture(string? culture) => culture ?? string.Empty;

    private static bool ProcessorArchitectureMatches(AssemblyName actual, AssemblyName expected)
    {
#pragma warning disable SYSLIB0037
        return expected.ProcessorArchitecture == ProcessorArchitecture.None
            || actual.ProcessorArchitecture == expected.ProcessorArchitecture;
#pragma warning restore SYSLIB0037
    }

    private static bool TokensEqual(byte[]? actual, byte[]? expected) =>
        (actual ?? []).AsSpan().SequenceEqual(expected ?? []);

    private static bool IsFrameworkAssembly(AssemblyName requested, string name)
    {
        if (!string.Equals(name, "netstandard", StringComparison.Ordinal)
            && !string.Equals(name, "System", StringComparison.Ordinal)
            && !name.StartsWith("System.", StringComparison.Ordinal)
            && !string.Equals(name, "Microsoft", StringComparison.Ordinal)
            && !name.StartsWith("Microsoft.", StringComparison.Ordinal))
        {
            return false;
        }

        var token = requested.GetPublicKeyToken();
        return token is { Length: > 0 }
            && FrameworkPublicKeyTokens.Contains(Convert.ToHexStringLower(token));
    }
}
