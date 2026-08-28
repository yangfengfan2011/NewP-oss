using System.Text.Json;

namespace ICP.Platform.Modeling.Metadata;

// 表单模式把交互可写性、调用方必填和创建初始值分开表达；这些 UI 事实不能改变存储可空性。
public sealed class ModelMetadataFormModeRule
{
    public string Mode { get; init; } = string.Empty;

    public bool Required { get; init; }

    public JsonElement? DefaultValue { get; init; }
}
