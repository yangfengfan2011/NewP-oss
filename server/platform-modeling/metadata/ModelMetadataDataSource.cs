namespace ICP.Platform.Modeling.Metadata;

// dataSource 只表达字段级静态数据来源，不承载查询脚本或动态过滤。
public sealed class ModelMetadataDataSource
{
    public string Type { get; init; } = string.Empty;

    public string? Key { get; init; }

    public string? Enum { get; init; }

    public string? Model { get; init; }
}
