namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 平台内建 migration stream 当前必须到达的稳定前沿。
/// </summary>
public static class PlatformCoreMigrationFrontiers
{
    /// <summary>platform.core SQL Server 在 Organization 永久绑定切换后的前沿。</summary>
    public const string SqlServerLegacyBaseline = "20260810102141_Organization_PermanentUserBinding";

    /// <summary>Identity 模块 SQL Server 基础 schema 前沿。</summary>
    public const string IdentitySqlServerFoundation = "20260810033908_Identity_000001_Foundation";

    /// <summary>Authentication 模块 SQL Server 基础 schema 前沿。</summary>
    public const string AuthenticationSqlServerFoundation = "20260810033909_Authentication_000001_Foundation";
}
