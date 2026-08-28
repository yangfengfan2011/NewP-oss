namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 平台内建 migration stream 当前必须到达的稳定前沿。
/// </summary>
public static class PlatformCoreMigrationFrontiers
{
    /// <summary>SQL Server 平台基础迁移流的当前前沿。</summary>
    public const string SqlServerPlatformFoundation = "20260812144154_PlatformFoundation";

    /// <summary>Identity 模块 SQL Server 基础 schema 前沿。</summary>
    public const string IdentitySqlServerFoundation = "20260810033908_Identity_000001_Foundation";

    /// <summary>Authentication 模块 SQL Server 基础 schema 前沿。</summary>
    public const string AuthenticationSqlServerFoundation = "20260828151500_Authentication_000001_Foundation";
}
