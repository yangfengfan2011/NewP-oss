using System.Globalization;

namespace ICP.Platform.Presentation;

/// <summary>按请求展示上下文格式化 UTC temporal 值。</summary>
public interface ITemporalDisplayFormatter
{
    /// <summary>将 UTC instant 转换为会话时区并按请求 locale 格式化。</summary>
    string FormatInstant(DateTime instantUtc);
}

/// <summary>共享的请求级 temporal display formatter。</summary>
public sealed class TemporalDisplayFormatter(IRequestPresentationContextAccessor contextAccessor)
    : ITemporalDisplayFormatter
{
    /// <inheritdoc />
    public string FormatInstant(DateTime instantUtc)
    {
        if (instantUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("TEMPORAL_INSTANT_UTC_REQUIRED");
        }

        var context = contextAccessor.Current;
        var culture = CultureInfo.GetCultureInfo(context.Locale);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZoneId);
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(instantUtc, timeZone);
        return localTime.ToString("G", culture);
    }
}
