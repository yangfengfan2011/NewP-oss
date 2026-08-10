namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 平台内建 migration stream 当前必须到达的稳定前沿。
/// </summary>
public static class PlatformCoreMigrationFrontiers
{
    /// <summary>platform.core SQL Server 在人员生命周期并发版本落库后的前沿。</summary>
    public const string SqlServerLegacyBaseline = "20260810111523_Organization_PersonLifecycleRevision";

    /// <summary>Identity 模块 SQL Server 基础 schema 前沿。</summary>
    public const string IdentitySqlServerFoundation = "20260810033908_Identity_000001_Foundation";

    /// <summary>Authentication 模块 SQL Server 基础 schema 前沿。</summary>
    public const string AuthenticationSqlServerFoundation = "20260810033909_Authentication_000001_Foundation";
}
