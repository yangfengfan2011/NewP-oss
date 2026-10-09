using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Planning;

/// <summary>
/// 把统一 planner 编译的 lookup display 计划投影为现有查询运行时 descriptor。
/// </summary>
public sealed class ModelDisplayValueFieldProjector
{
    /// <summary>
    /// 拒绝失败或部分编译结果，只把成功计划交给 runtime descriptor 适配。
    /// </summary>
    /// <param name="compilationResult">统一 planner 返回的完整编译结果。</param>
    /// <returns>成功计划中的 lookup runtime descriptors。</returns>
    public IReadOnlyList<PlatformDisplayValueField> Project(
        ModelDisplayProjectionCompilationResult compilationResult)
    {
        ArgumentNullException.ThrowIfNull(compilationResult);
        if (!compilationResult.IsSuccess)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                compilationResult.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.ModelName}.{diagnostic.FieldName}: {diagnostic.Message}")));
        }

        return Project(compilationResult.Plan!);
    }

    /// <summary>
    /// 只消费 typed lookup plan；temporal 等其他策略由各自 runtime consumer 持有。
    /// </summary>
    /// <param name="projectionPlan">统一 planner 成功编译的 typed display plan。</param>
    /// <returns>仅包含 reference/user lookup 的 runtime descriptors。</returns>
    public IReadOnlyList<PlatformDisplayValueField> Project(
        ModelDisplayProjectionPlan projectionPlan)
    {
        ArgumentNullException.ThrowIfNull(projectionPlan);

        return projectionPlan.Fields
            .OfType<LookupFieldDisplayProjectionPlan>()
            .Select(ProjectField)
            .ToArray();
    }

    // typed plan 已闭合策略；两个读取入口共用相同的查询字段映射。
    internal static PlatformDisplayValueField ProjectField(FieldDisplayProjectionPlan plan) => plan switch
    {
        LookupFieldDisplayProjectionPlan lookup => new(lookup.CompanionField, lookup.SourceField,
            lookup.TargetModel, lookup.TargetIdField, lookup.TargetDisplayField),
        TemporalFieldDisplayProjectionPlan or CalendarDateFieldDisplayProjectionPlan =>
            new(plan.CompanionField, plan.SourceField, "$temporal", plan.SourceField, plan.CompanionField),
        _ => throw new InvalidOperationException($"DISPLAY_PLAN_UNSUPPORTED: {plan.SourceField}")
    };
}
