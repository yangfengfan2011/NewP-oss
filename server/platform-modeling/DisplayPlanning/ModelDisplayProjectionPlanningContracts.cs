using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.DisplayPlanning;

/// <summary>
/// 编译 Model 字段 display 声明，并返回唯一的 typed projection plan 或稳定诊断。
/// </summary>
public interface IModelDisplayProjectionPlanner
{
    /// <summary>
    /// 从 Model authoring 编译 display plan，并保留内核固定字段的 authoring 展示声明。
    /// </summary>
    /// <param name="sourceDocument">拥有原始字段与 display 声明的 authoring 文档。</param>
    /// <param name="modelCatalog">可选的候选模型 catalog；非空时必须包含 reference 目标模型。</param>
    /// <returns>成功时返回完整计划，失败时只返回可定位诊断，不返回部分计划。</returns>
    ModelDisplayProjectionCompilationResult Compile(
        ModelMetadataDocument sourceDocument,
        IReadOnlyCollection<ModelDefinition>? modelCatalog = null);

    /// <summary>
    /// 从规范 Model definition 编译 display plan；传入 catalog 时同时闭合 reference 目标字段。
    /// </summary>
    /// <param name="sourceModel">拥有原始字段与 display 声明的源模型。</param>
    /// <param name="modelCatalog">可选的候选模型 catalog；非空时必须包含 reference 目标模型。</param>
    /// <returns>成功时返回完整计划，失败时只返回可定位诊断，不返回部分计划。</returns>
    ModelDisplayProjectionCompilationResult Compile(
        ModelDefinition sourceModel,
        IReadOnlyCollection<ModelDefinition>? modelCatalog = null);
}

/// <summary>
/// 单个字段的规范 display projection plan；companion 始终为 output-only string|null。
/// </summary>
public abstract record FieldDisplayProjectionPlan(
    string SourceField,
    string CompanionField);

/// <summary>
/// lookup projection 的受控策略种类。
/// </summary>
public enum LookupDisplayProjectionKind
{
    Reference,
    User
}

/// <summary>
/// reference/user lookup 所需的完整目标信息。
/// </summary>
public sealed record LookupFieldDisplayProjectionPlan(
    string SourceField,
    string CompanionField,
    LookupDisplayProjectionKind Kind,
    string TargetModel,
    string TargetIdField,
    string TargetDisplayField)
    : FieldDisplayProjectionPlan(SourceField, CompanionField);

/// <summary>
/// temporal projection 允许的标准输出格式。
/// </summary>
public enum TemporalDisplayFormat
{
    Date,
    DateTime
}

/// <summary>
/// instant temporal projection 的受控格式计划。
/// </summary>
public sealed record TemporalFieldDisplayProjectionPlan(
    string SourceField,
    string CompanionField,
    TemporalDisplayFormat Format)
    : FieldDisplayProjectionPlan(SourceField, CompanionField);

/// <summary>
/// 日历日期投影保留原始日期，不应用任何时区转换。
/// </summary>
public sealed record CalendarDateFieldDisplayProjectionPlan(
    string SourceField,
    string CompanionField)
    : FieldDisplayProjectionPlan(SourceField, CompanionField);

/// <summary>
/// 一个 Model 的完整 display projection plan。
/// </summary>
public sealed record ModelDisplayProjectionPlan(
    string ModelName,
    IReadOnlyList<FieldDisplayProjectionPlan> Fields);

/// <summary>
/// display projection plan 编译的稳定、字段可定位失败信号。
/// </summary>
public sealed record ModelDisplayProjectionDiagnostic(
    string Code,
    string ModelName,
    string FieldName,
    string Message);

/// <summary>
/// display projection plan 编译结果；失败时不返回部分计划。
/// </summary>
public sealed record ModelDisplayProjectionCompilationResult(
    ModelDisplayProjectionPlan? Plan,
    IReadOnlyList<ModelDisplayProjectionDiagnostic> Diagnostics)
{
    public bool IsSuccess => Plan is not null && Diagnostics.Count == 0;
}

/// <summary>
/// display projection plan 编译的稳定诊断码。
/// </summary>
public static class ModelDisplayProjectionDiagnosticCodes
{
    public const string TypeUnsupported = "MODEL_DISPLAY_TYPE_UNSUPPORTED";
    public const string SourceSemanticsMismatch = "MODEL_DISPLAY_SOURCE_SEMANTICS_MISMATCH";
    public const string LookupParametersRequired = "MODEL_DISPLAY_LOOKUP_PARAMETERS_REQUIRED";
    public const string UserTargetInvalid = "MODEL_DISPLAY_USER_TARGET_INVALID";
    public const string TemporalFormatRequired = "MODEL_DISPLAY_TEMPORAL_FORMAT_REQUIRED";
    public const string ParametersMutuallyExclusive = "MODEL_DISPLAY_PARAMETERS_MUTUALLY_EXCLUSIVE";
    public const string CompanionNameConflict = "MODEL_DISPLAY_COMPANION_NAME_CONFLICT";
    public const string TargetModelMissing = "MODEL_DISPLAY_TARGET_MODEL_MISSING";
    public const string TargetModelAmbiguous = "MODEL_DISPLAY_TARGET_MODEL_AMBIGUOUS";
    public const string TargetFieldMissing = "MODEL_DISPLAY_TARGET_FIELD_MISSING";
}
