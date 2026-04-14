namespace ICP.Platform.Modeling.Metadata;

// 表单模式当前只支持 hidden / readonly / editable 这类静态值。
public sealed class ModelMetadataFormModeRule
{
    public string Mode { get; init; } = string.Empty;
}
