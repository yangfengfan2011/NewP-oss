namespace ICP.Platform.Modeling.Metadata;

public sealed class StorageTypeMapDocument
{
    public string SchemaVersion { get; init; } = "1.0";

    // key 就是模型元数据里可写的 storage.type。
    public Dictionary<string, StorageTypeMapEntry> Types { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StorageTypeMapEntry
{
    public StorageTypeCapabilities Capabilities { get; init; } = new();

    // key 是持久化 provider 名称，例如 SqlServer / Oracle / MySql。
    public Dictionary<string, StorageTypeProviderMap> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StorageTypeCapabilities
{
    public bool SupportsLength { get; init; }

    public bool SupportsPrecision { get; init; }

    public bool SupportsScale { get; init; }
}

public sealed class StorageTypeProviderMap
{
    public string StoreType { get; init; } = string.Empty;

    public int? DefaultLength { get; init; }

    public int? DefaultPrecision { get; init; }

    public int? DefaultScale { get; init; }
}
