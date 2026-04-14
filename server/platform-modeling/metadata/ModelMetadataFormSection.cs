namespace ICP.Platform.Modeling.Metadata;

// form 只描述 create/edit 两个场景的静态行为模式。
public sealed class ModelMetadataFormSection
{
    public ModelMetadataFormModeRule? Create { get; init; }

    public ModelMetadataFormModeRule? Edit { get; init; }
}
