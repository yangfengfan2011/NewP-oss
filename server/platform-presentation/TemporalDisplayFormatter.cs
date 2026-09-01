using System.Globalization;

namespace ICP.Platform.Presentation;

public interface ITemporalDisplayFormatter
{
    string FormatInstant(DateTime instantUtc);
}

public sealed class TemporalDisplayFormatter(IRequestPresentationContextAccessor contextAccessor)
    : ITemporalDisplayFormatter
{
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
