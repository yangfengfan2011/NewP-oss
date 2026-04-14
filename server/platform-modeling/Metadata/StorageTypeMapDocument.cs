namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 表示平台存储类型映射文档。
/// </summary>
public sealed class StorageTypeMapDocument
{
    /// <summary>
    /// 获取或设置文档版本。
    /// </summary>
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置平台类型定义集合，键为 storage.type。
    /// </summary>
    public Dictionary<string, StorageTypeDefinition> Types { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 表示单个平台存储类型定义。
/// </summary>
public sealed class StorageTypeDefinition
{
    /// <summary>
    /// 获取或设置能力约束定义。
    /// </summary>
    public StorageTypeCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// 获取或设置不同 provider 下的存储映射。
    /// </summary>
    public Dictionary<string, StorageProviderDefinition> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 表示存储类型支持的约束能力。
/// </summary>
public sealed class StorageTypeCapabilities
{
    /// <summary>
    /// 获取或设置该类型是否支持长度。
    /// </summary>
    public bool SupportsLength { get; set; }

    /// <summary>
    /// 获取或设置该类型是否支持精度。
    /// </summary>
    public bool SupportsPrecision { get; set; }

    /// <summary>
    /// 获取或设置该类型是否支持小数位。
    /// </summary>
    public bool SupportsScale { get; set; }
}

/// <summary>
/// 表示某个数据库 provider 下的默认存储映射。
/// </summary>
public sealed class StorageProviderDefinition
{
    /// <summary>
    /// 获取或设置数据库存储类型名。
    /// </summary>
    public string StoreType { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置默认长度。
    /// </summary>
    public int? DefaultLength { get; set; }

    /// <summary>
    /// 获取或设置默认精度。
    /// </summary>
    public int? DefaultPrecision { get; set; }

    /// <summary>
    /// 获取或设置默认小数位。
    /// </summary>
    public int? DefaultScale { get; set; }
}
