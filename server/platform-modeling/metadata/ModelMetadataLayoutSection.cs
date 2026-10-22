namespace ICP.Platform.Modeling.Metadata;

// layout 只服务于 schema 生成，不能泄漏到数据库投影链路。
public sealed class ModelMetadataLayoutSection
{
    public string Position { get; init; } = string.Empty;

    public int Width { get; init; }

    public string? Section { get; init; }
}
