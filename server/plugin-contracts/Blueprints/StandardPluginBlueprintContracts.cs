namespace ICP.Platform.Plugin.Contracts;

/// <summary>
/// standard-data.v1 的根 authoring 真源。
/// </summary>
public sealed record StandardPluginBlueprint(
    string SchemaVersion,
    string Profile,
    string ModuleId,
    string DisplayName,
    string Version,
    string? Description,
    StandardPluginCompatibility Compatibility,
    StandardDataBlueprint StandardData,
    StandardNavigationBlueprint Navigation,
    StandardPersistenceBlueprint Persistence);

/// <summary>
/// 插件声明的 Platform API 与持久化兼容边界。
/// </summary>
public sealed record StandardPluginCompatibility(
    PlatformApiVersionRange PlatformApi,
    StandardPersistenceCompatibility Persistence);

/// <summary>
/// Platform API 同 major 的显式版本范围。
/// </summary>
public sealed record PlatformApiVersionRange(string MinInclusive, string MaxExclusive);

/// <summary>
/// standard-data.v1 允许的持久化契约和 provider。
/// </summary>
public sealed record StandardPersistenceCompatibility(
    string Contract,
    IReadOnlyList<string> Providers);

/// <summary>
/// 由平台派生标准 CRUD 行为的数据资源集合。
/// </summary>
public sealed record StandardDataBlueprint(IReadOnlyList<StandardDataResourceBlueprint> Resources);

/// <summary>
/// 单个标准 CRUD 资源的 authoring 声明。
/// </summary>
public sealed record StandardDataResourceBlueprint(
    string Key,
    string PluralName,
    string DisplayName,
    string DisplayField,
    IReadOnlyList<StandardDataFieldBlueprint> Fields,
    string? DetailViewKey = null);

/// <summary>
/// 标准数据资源字段声明。
/// </summary>
public sealed record StandardDataFieldBlueprint(
    string Name,
    string Type,
    bool Required,
    bool Unique = false);

/// <summary>
/// 标准页面在平台公开 slot 中的导航声明。
/// </summary>
public sealed record StandardNavigationBlueprint(
    string Slot,
    string Group,
    string Menu,
    int Order);

/// <summary>
/// 与 Canonical Module Plan 共用的模型与 provider migration bundle 声明。
/// </summary>
public sealed record StandardPersistenceBlueprint(
    IReadOnlyList<string> Models,
    IReadOnlyDictionary<string, StandardMigrationBundleBlueprint> Migrations);

/// <summary>
/// 单个 provider migration bundle 的包内相对路径。
/// </summary>
public sealed record StandardMigrationBundleBlueprint(string Assembly, string Manifest);

/// <summary>
/// 单个资源的确定性 API、页面和物理存储投影。
/// </summary>
public sealed record StandardDataResourceProjection(
    string ResourceKey,
    string ApiRoute,
    string PageRoute,
    string SchemaName,
    string TableName,
    IReadOnlyList<string> UniqueIndexes,
    string? DetailViewKey = null);

/// <summary>
/// standard-data.v1 的编译投影。
/// </summary>
public sealed record StandardPluginProjection(
    string ModuleSlug,
    IReadOnlyList<StandardDataResourceProjection> Resources);

/// <summary>
/// Blueprint、构建证明与派生投影组成的不可变编译结果。
/// </summary>
public sealed record StandardPluginCompilation(
    StandardPluginBlueprint Blueprint,
    PlatformApiAttestation Attestation,
    StandardPluginProjection Projection);

/// <summary>
/// Blueprint 编译结果；失败时不返回部分投影。
/// </summary>
public sealed record StandardPluginCompilationResult(
    StandardPluginCompilation? Compilation,
    IReadOnlyList<PlatformApiDiagnostic> Diagnostics)
{
    public bool IsSuccess => Compilation is not null && Diagnostics.Count == 0;
}
