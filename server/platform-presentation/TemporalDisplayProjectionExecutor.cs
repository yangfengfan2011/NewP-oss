using ICP.Platform.Modeling.DisplayPlanning;

namespace ICP.Platform.Presentation;

/// <summary>执行 typed temporal projection plan，并保留原始 UTC 时间点。</summary>
public interface ITemporalDisplayProjectionExecutor
{
    /// <summary>为一个 UTC instant 形成 raw/display 成对投影。</summary>
    /// <param name="plan">统一 planner 编译出的 temporal 字段计划。</param>
    /// <param name="rawValue">调用方拥有的原始业务时间点，必须为 UTC Kind。</param>
    /// <returns>原始时间点及可空的请求级显示值。</returns>
    TemporalDisplayProjectionResult Project(
        TemporalFieldDisplayProjectionPlan plan,
        DateTime rawValue);
}

/// <summary>保留业务 raw 并携带 canonical string|null display 的 temporal 投影结果。</summary>
public sealed record TemporalDisplayProjectionResult(
    DateTime RawValue,
    string? DisplayValue);

/// <summary>在公共 presentation 边界执行 typed temporal plan。</summary>
public sealed class TemporalDisplayProjectionExecutor(
    IRequestPresentationContextAccessor contextAccessor,
    ITemporalDisplayFormatter formatter) : ITemporalDisplayProjectionExecutor
{
    /// <inheritdoc />
    public TemporalDisplayProjectionResult Project(
        TemporalFieldDisplayProjectionPlan plan,
        DateTime rawValue)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (rawValue.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("TEMPORAL_INSTANT_UTC_REQUIRED");
        }

        var context = contextAccessor.Current;
        return context is null
            ? new TemporalDisplayProjectionResult(rawValue, null)
            : new TemporalDisplayProjectionResult(
                rawValue,
                formatter.FormatInstant(rawValue, plan.Format, context));
    }
}
