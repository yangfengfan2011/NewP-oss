namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 统一 platform.core migration baseline 当前必须到达的稳定前沿。
/// </summary>
public static class PlatformCoreMigrationFrontiers
{
    /// <summary>包含 Identity 与 Authentication 基础 schema 的 SQL Server 前沿。</summary>
    public const string SqlServerIdentityFoundation = "20260810033908_AddIdentityFoundation";
}
