using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;

namespace ICP.Platform.Modeling.DisplayPlanning;

/// <summary>
/// 统一拥有 display.type、source 语义、策略参数、companion 资格与 catalog 目标校验。
/// </summary>
public sealed class ModelDisplayProjectionPlanner : IModelDisplayProjectionPlanner
{
    /// <inheritdoc />
    public ModelDisplayProjectionCompilationResult Compile(
        ModelMetadataDocument sourceDocument,
        IReadOnlyCollection<ModelDefinition>? modelCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        if (string.Equals(sourceDocument.Model.Kind, "Entity", StringComparison.OrdinalIgnoreCase))
        {
            var fixedFields = new ModelMetadataProjector().ProjectFixedFields(sourceDocument);
            var conflicts = sourceDocument.Model.Fields.Where(field => field.Contract?.ValueType is { Length: > 0 }
                && fixedFields.Any(fixedField => fixedField.IsAuditField
                    && string.Equals(fixedField.Name, field.Name, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(fixedField.ValueType.ToString(), field.Contract.ValueType, StringComparison.OrdinalIgnoreCase)))
                .Select(field => new ModelDisplayProjectionDiagnostic("MODEL_AUDIT_DISPLAY_CONFLICT", sourceDocument.Model.Name,
                    field.Name, $"Field '{field.Name}' conflicts with the platform audit value semantics.")).ToArray();
            if (conflicts.Length > 0)
            {
                return new ModelDisplayProjectionCompilationResult(null, conflicts);
            }
        }
        return Compile(new ModelMetadataProjector().Project(sourceDocument), modelCatalog);
    }

    /// <inheritdoc />
    public ModelDisplayProjectionCompilationResult Compile(
        ModelDefinition sourceModel,
        IReadOnlyCollection<ModelDefinition>? modelCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(sourceModel);

        var plans = new List<FieldDisplayProjectionPlan>();
        var diagnostics = new List<ModelDisplayProjectionDiagnostic>();
        var fixedFields = new ModelMetadataProjector().ProjectFixedFields(new ModelMetadataDocument
        {
            Model = new ModelMetadataModel { Name = sourceModel.Name, Kind = sourceModel.Kind.ToString() }
        });
        foreach (var field in sourceModel.Fields.Where(static field => field.DisplayRule is not null))
        {
            var fixedField = fixedFields.SingleOrDefault(item => item.Name == field.Name);
            if (sourceModel.Kind == ModelKind.Entity && fixedField?.IsAuditField == true
                && (field.ValueType != fixedField.ValueType || !MatchesAuditDisplay(field.DisplayRule!, fixedField.DisplayRule!)))
            {
                diagnostics.Add(new ModelDisplayProjectionDiagnostic("MODEL_AUDIT_DISPLAY_CONFLICT", sourceModel.Name, field.Name,
                    $"Field '{field.Name}' conflicts with the platform audit display rule."));
                continue;
            }
            var fieldResult = CompileField(sourceModel, field, modelCatalog);
            if (fieldResult.Plan is not null)
            {
                plans.Add(fieldResult.Plan with { IsEntityAuditField = sourceModel.Kind == ModelKind.Entity && field.IsAuditField });
            }

            if (fieldResult.Diagnostic is not null)
            {
                diagnostics.Add(fieldResult.Diagnostic);
            }
        }

        if (diagnostics.Count > 0)
        {
            return new ModelDisplayProjectionCompilationResult(
                null,
                Array.AsReadOnly(diagnostics.ToArray()));
        }

        return new ModelDisplayProjectionCompilationResult(
            new ModelDisplayProjectionPlan(
                sourceModel.Name,
                Array.AsReadOnly(plans.ToArray())),
            Array.Empty<ModelDisplayProjectionDiagnostic>());
    }

    // 类型和格式沿用 planner 的大小写语义；目标字段名仍须与规范目录完全相同。
    private static bool MatchesAuditDisplay(FieldDisplayRuleDefinition actual, FieldDisplayRuleDefinition expected) =>
        string.Equals(actual.Type, expected.Type, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.Format, expected.Format, StringComparison.OrdinalIgnoreCase)
        && actual.TargetModel == expected.TargetModel
        && actual.TargetIdField == expected.TargetIdField
        && actual.TargetDisplayField == expected.TargetDisplayField;

    // 每个字段只进入一个已知策略；未知 type 不得落入任何兼容分支。
    private static FieldCompilationResult CompileField(
        ModelDefinition sourceModel,
        FieldDefinition field,
        IReadOnlyCollection<ModelDefinition>? modelCatalog)
    {
        var display = field.DisplayRule!;
        if (string.Equals(display.Type, "reference", StringComparison.OrdinalIgnoreCase))
        {
            return CompileReference(sourceModel, field, display, modelCatalog);
        }

        if (string.Equals(display.Type, "user", StringComparison.OrdinalIgnoreCase))
        {
            return CompileUser(sourceModel, field, display);
        }

        if (string.Equals(display.Type, "instant", StringComparison.OrdinalIgnoreCase))
        {
            return CompileInstant(sourceModel, field, display);
        }

        if (string.Equals(display.Type, "calendarDate", StringComparison.OrdinalIgnoreCase))
        {
            return CompileCalendarDate(sourceModel, field, display);
        }

        return Failure(
            ModelDisplayProjectionDiagnosticCodes.TypeUnsupported,
            sourceModel,
            field,
            $"Field '{field.Name}' has unsupported display.type '{display.Type}'.");
    }

    // reference 策略闭合 source、参数与可用 catalog 目标，不替调用方猜测默认 target。
    private static FieldCompilationResult CompileReference(
        ModelDefinition sourceModel,
        FieldDefinition field,
        FieldDisplayRuleDefinition display,
        IReadOnlyCollection<ModelDefinition>? modelCatalog)
    {
        if (field.Kind != FieldKind.Reference)
        {
            return SourceMismatch(sourceModel, field, display.Type);
        }

        if (!string.IsNullOrWhiteSpace(display.Format))
        {
            return MutuallyExclusive(sourceModel, field, display.Type);
        }

        if (string.IsNullOrWhiteSpace(display.TargetModel)
            || string.IsNullOrWhiteSpace(display.TargetIdField)
            || string.IsNullOrWhiteSpace(display.TargetDisplayField))
        {
            return Failure(
                ModelDisplayProjectionDiagnosticCodes.LookupParametersRequired,
                sourceModel,
                field,
                $"Field '{field.Name}' reference display must define non-empty targetModel, targetIdField, and targetDisplayField.");
        }

        var companionConflict = ValidateCompanionName(sourceModel, field);
        if (companionConflict is not null)
        {
            return companionConflict;
        }

        if (modelCatalog is not null)
        {
            var catalogFailure = ValidateReferenceTarget(sourceModel, field, display, modelCatalog);
            if (catalogFailure is not null)
            {
                return catalogFailure;
            }
        }

        return Success(new LookupFieldDisplayProjectionPlan(
            field.Name,
            CompanionField(field),
            LookupDisplayProjectionKind.Reference,
            display.TargetModel,
            display.TargetIdField,
            display.TargetDisplayField));
    }

    // user 策略固定 IdentityAccount/EID/ENAME，保持 resolver owner 与公共语义唯一。
    private static FieldCompilationResult CompileUser(
        ModelDefinition sourceModel,
        FieldDefinition field,
        FieldDisplayRuleDefinition display)
    {
        if (field.ValueType != FieldValueType.User)
        {
            return SourceMismatch(sourceModel, field, display.Type);
        }

        if (!string.IsNullOrWhiteSpace(display.Format))
        {
            return MutuallyExclusive(sourceModel, field, display.Type);
        }

        if (!string.Equals(display.TargetModel, "IdentityAccount", StringComparison.Ordinal)
            || !string.Equals(display.TargetIdField, "EID", StringComparison.Ordinal)
            || !string.Equals(display.TargetDisplayField, "ENAME", StringComparison.Ordinal))
        {
            return Failure(
                ModelDisplayProjectionDiagnosticCodes.UserTargetInvalid,
                sourceModel,
                field,
                $"Field '{field.Name}' user display must target IdentityAccount/EID/ENAME.");
        }

        var companionConflict = ValidateCompanionName(sourceModel, field);
        if (companionConflict is not null)
        {
            return companionConflict;
        }

        return Success(new LookupFieldDisplayProjectionPlan(
            field.Name,
            CompanionField(field),
            LookupDisplayProjectionKind.User,
            display.TargetModel,
            display.TargetIdField,
            display.TargetDisplayField));
    }

    // instant 策略只接受受控标准格式，不复用 lookup target 参数表达 temporal 计划。
    private static FieldCompilationResult CompileInstant(
        ModelDefinition sourceModel,
        FieldDefinition field,
        FieldDisplayRuleDefinition display)
    {
        if (field.ValueType != FieldValueType.Instant)
        {
            return SourceMismatch(sourceModel, field, display.Type);
        }

        if (!string.IsNullOrWhiteSpace(display.TargetModel)
            || !string.IsNullOrWhiteSpace(display.TargetIdField)
            || !string.IsNullOrWhiteSpace(display.TargetDisplayField))
        {
            return MutuallyExclusive(sourceModel, field, display.Type);
        }

        var format = display.Format switch
        {
            "date" => TemporalDisplayFormat.Date,
            "dateTime" => TemporalDisplayFormat.DateTime,
            _ => (TemporalDisplayFormat?)null
        };
        if (format is null)
        {
            return Failure(
                ModelDisplayProjectionDiagnosticCodes.TemporalFormatRequired,
                sourceModel,
                field,
                $"Field '{field.Name}' instant display must use format 'dateTime' or 'date' without a target model.");
        }

        var companionConflict = ValidateCompanionName(sourceModel, field);
        if (companionConflict is not null)
        {
            return companionConflict;
        }

        return Success(new TemporalFieldDisplayProjectionPlan(
            field.Name,
            CompanionField(field),
            format.Value));
    }

    // calendarDate 只允许 date 展示格式，并且不接受任何 lookup 目标参数。
    private static FieldCompilationResult CompileCalendarDate(
        ModelDefinition sourceModel,
        FieldDefinition field,
        FieldDisplayRuleDefinition display)
    {
        if (field.ValueType != FieldValueType.CalendarDate)
        {
            return SourceMismatch(sourceModel, field, display.Type);
        }

        if (!string.IsNullOrWhiteSpace(display.TargetModel)
            || !string.IsNullOrWhiteSpace(display.TargetIdField)
            || !string.IsNullOrWhiteSpace(display.TargetDisplayField))
        {
            return MutuallyExclusive(sourceModel, field, display.Type);
        }

        if (!string.Equals(display.Format, "date", StringComparison.Ordinal))
        {
            return Failure(
                ModelDisplayProjectionDiagnosticCodes.TemporalFormatRequired,
                sourceModel,
                field,
                $"Field '{field.Name}' calendarDate display must use format 'date' without a target model.");
        }

        var companionConflict = ValidateCompanionName(sourceModel, field);
        return companionConflict ?? Success(new CalendarDateFieldDisplayProjectionPlan(
            field.Name,
            CompanionField(field)));
    }

    // companion 是成功 plan 的派生协议；任何 authored 同名字段都必须拒绝。
    private static FieldCompilationResult? ValidateCompanionName(
        ModelDefinition sourceModel,
        FieldDefinition field)
    {
        var companionField = CompanionField(field);
        return sourceModel.Fields.Any(candidate =>
                string.Equals(candidate.Name, companionField, StringComparison.OrdinalIgnoreCase))
            ? Failure(
                ModelDisplayProjectionDiagnosticCodes.CompanionNameConflict,
                sourceModel,
                field,
                $"Field '{companionField}' conflicts with the generated display companion for '{field.Name}'.")
            : null;
    }

    // catalog 校验只在调用方提供完整候选时启用，authoring 单文档校验不虚构跨模型事实。
    private static FieldCompilationResult? ValidateReferenceTarget(
        ModelDefinition sourceModel,
        FieldDefinition field,
        FieldDisplayRuleDefinition display,
        IReadOnlyCollection<ModelDefinition> modelCatalog)
    {
        var matches = modelCatalog
            .Where(model => string.Equals(model.Name, display.TargetModel, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return Failure(
                ModelDisplayProjectionDiagnosticCodes.TargetModelMissing,
                sourceModel,
                field,
                $"Reference display for field '{field.Name}' targets missing target model '{display.TargetModel}'.");
        }

        if (matches.Length > 1)
        {
            return Failure(
                ModelDisplayProjectionDiagnosticCodes.TargetModelAmbiguous,
                sourceModel,
                field,
                $"Reference display for field '{field.Name}' targets ambiguous model '{display.TargetModel}'.");
        }

        var targetModel = matches[0];
        if (!HasField(targetModel, display.TargetIdField))
        {
            return MissingTargetField(sourceModel, field, targetModel, display.TargetIdField, "id");
        }

        return HasField(targetModel, display.TargetDisplayField)
            ? null
            : MissingTargetField(sourceModel, field, targetModel, display.TargetDisplayField, "display");
    }

    // 目标字段匹配保持当前 Model catalog 的大小写敏感语义。
    private static bool HasField(ModelDefinition model, string fieldName) =>
        model.Fields.Any(field => string.Equals(field.Name, fieldName, StringComparison.Ordinal));

    // target field 缺失使用同一稳定诊断形状，并保留字段角色方便定位。
    private static FieldCompilationResult MissingTargetField(
        ModelDefinition sourceModel,
        FieldDefinition sourceField,
        ModelDefinition targetModel,
        string targetFieldName,
        string targetFieldRole) =>
        Failure(
            ModelDisplayProjectionDiagnosticCodes.TargetFieldMissing,
            sourceModel,
            sourceField,
            $"Reference display for field '{sourceField.Name}' targets model '{targetModel.Name}' without {targetFieldRole} field '{targetFieldName}'.");

    // source semantics 失败对所有策略使用统一可定位诊断。
    private static FieldCompilationResult SourceMismatch(
        ModelDefinition sourceModel,
        FieldDefinition field,
        string displayType) =>
        Failure(
            ModelDisplayProjectionDiagnosticCodes.SourceSemanticsMismatch,
            sourceModel,
            field,
            $"Field '{field.Name}' must match display.type '{displayType}' source semantics.");

    // lookup 与 temporal 参数互斥，避免可选字符串把字段默认为另一策略。
    private static FieldCompilationResult MutuallyExclusive(
        ModelDefinition sourceModel,
        FieldDefinition field,
        string displayType) =>
        Failure(
            ModelDisplayProjectionDiagnosticCodes.ParametersMutuallyExclusive,
            sourceModel,
            field,
            $"Field '{field.Name}' display.type '{displayType}' contains mutually exclusive strategy parameters.");

    // companion 名称只由成功 plan 的 canonical 后缀规则派生。
    private static string CompanionField(FieldDefinition field) => $"{field.Name}_DisplayValue";

    // 成功结果只携带一种 typed field plan。
    private static FieldCompilationResult Success(FieldDisplayProjectionPlan plan) => new(plan, null);

    // 失败结果不携带部分字段计划。
    private static FieldCompilationResult Failure(
        string code,
        ModelDefinition sourceModel,
        FieldDefinition field,
        string message) =>
        new(null, new ModelDisplayProjectionDiagnostic(code, sourceModel.Name, field.Name, message));

    private sealed record FieldCompilationResult(
        FieldDisplayProjectionPlan? Plan,
        ModelDisplayProjectionDiagnostic? Diagnostic);
}
