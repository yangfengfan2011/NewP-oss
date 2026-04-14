namespace ICP.Platform.Modeling.Metadata;

// 字段元数据可同时包含存储和 UI authoring 分区，但只有 storage 能影响数据库投影。
public sealed class ModelMetadataField
{
    public string Name { get; init; } = string.Empty;

    public ModelMetadataStorageSection? Storage { get; init; }

    public string? UiType { get; init; }

    public ModelMetadataLayoutSection? Layout { get; init; }

    public ModelMetadataFormSection? Form { get; init; }

    public ModelMetadataViewSection? View { get; init; }

    public ModelMetadataDataSource? DataSource { get; init; }
}
