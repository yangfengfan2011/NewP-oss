namespace ICP.Platform.Plugin.Contracts;

/// <summary>
/// 一次 Platform API Release 必须原子产生的四类发布物。
/// </summary>
public static class PlatformApiArtifactKinds
{
    public const string Contracts = "contracts";
    public const string BackendSdk = "backend-sdk";
    public const string FrontendSdk = "frontend-sdk";
    public const string Tool = "tool";

    public static IReadOnlyList<string> Required { get; } =
    [
        Contracts,
        BackendSdk,
        FrontendSdk,
        Tool
    ];

    /// <summary>
    /// 返回发布物类型对应的不可变 package ID。
    /// </summary>
    public static string PackageIdFor(string kind) => kind switch
    {
        Contracts => "ICP.Platform.Plugin.Contracts",
        BackendSdk => "ICP.Platform.Plugin.Sdk",
        FrontendSdk => "@icp-platform/plugin-sdk",
        Tool => "ICP.Platform.Plugin.Tool",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Platform API artifact kind.")
    };
}
