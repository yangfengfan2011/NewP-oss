using System.Globalization;
using ICP.Platform.Modeling.DisplayPlanning;

namespace ICP.Platform.Presentation;

/// <summary>按请求展示上下文格式化 UTC temporal 值。</summary>
public interface ITemporalDisplayFormatter
{
    /// <summary>将 UTC instant 转换为会话时区，并按 typed plan 的受控标准格式输出。</summary>
    /// <param name="instantUtc">必须为 UTC Kind 的原始时间点。</param>
    /// <param name="format">统一 projection plan 编译出的受控日期或日期时间格式。</param>
    /// <param name="context">当前请求已校验的 locale 与可信会话时区。</param>
    /// <returns>按请求展示上下文形成的时间文本。</returns>
    string FormatInstant(
        DateTime instantUtc,
        TemporalDisplayFormat format,
        RequestPresentationContext context);
}

/// <summary>共享的请求级 temporal display formatter。</summary>
public sealed class TemporalDisplayFormatter : ITemporalDisplayFormatter
{
    /// <inheritdoc />
    public string FormatInstant(
        DateTime instantUtc,
        TemporalDisplayFormat format,
        RequestPresentationContext context)
    {
        if (instantUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("TEMPORAL_INSTANT_UTC_REQUIRED");
        }

        ArgumentNullException.ThrowIfNull(context);
        var culture = CultureInfo.GetCultureInfo(context.Locale);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZoneId);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(instantUtc, timeZone);
        var standardFormat = format switch
        {
            TemporalDisplayFormat.Date => "d",
            TemporalDisplayFormat.DateTime => "G",
            _ => throw new InvalidOperationException("TEMPORAL_DISPLAY_FORMAT_UNSUPPORTED")
        };
        return localTime.ToString(standardFormat, culture);
    }
}
