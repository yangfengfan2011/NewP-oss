using System.Globalization;
using System.Text.Json;
using ICP.Platform.Modeling;

namespace ICP.Platform.Presentation;

/// <summary>把标准 CRUD 的日历日期与本地时间输入规范为持久化值。</summary>
public interface ITemporalInputNormalizer
{
    TemporalInputNormalizationResult Normalize(FieldValueType valueType, object? value);

    DateOnly GetCurrentLocalDate();
}

/// <summary>temporal 输入规范化结果；失败包含可稳定断言的错误码。</summary>
public sealed record TemporalInputNormalizationResult(
    object? Value,
    string? ErrorCode,
    string? Message)
{
    public bool IsSuccess => ErrorCode is null;

    public static TemporalInputNormalizationResult Success(object? value) => new(value, null, null);

    public static TemporalInputNormalizationResult Failure(string code, string message) => new(null, code, message);
}

/// <summary>使用可信会话时区或部署默认时区解释 temporal 输入。</summary>
public sealed class TemporalInputNormalizer(
    IRequestPresentationContextAccessor contextAccessor,
    string defaultTimeZoneId,
    TimeProvider timeProvider) : ITemporalInputNormalizer
{
    public TemporalInputNormalizationResult Normalize(FieldValueType valueType, object? value)
    {
        if (value is null)
        {
            return TemporalInputNormalizationResult.Success(null);
        }

        return valueType switch
        {
            FieldValueType.CalendarDate => NormalizeCalendarDate(value),
            FieldValueType.Instant => NormalizeInstant(value),
            _ => TemporalInputNormalizationResult.Success(value)
        };
    }

    public DateOnly GetCurrentLocalDate()
    {
        var timeZone = ResolveTimeZone();
        var utcNow = timeProvider.GetUtcNow();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);
    }

    private static TemporalInputNormalizationResult NormalizeCalendarDate(object value)
    {
        if (value is DateOnly date)
        {
            return TemporalInputNormalizationResult.Success(date);
        }

        var text = ReadString(value);
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            ? TemporalInputNormalizationResult.Success(date)
            : TemporalInputNormalizationResult.Failure("TEMPORAL_INPUT_INVALID", "日历日期必须使用 yyyy-MM-dd 格式。");
    }

    private TemporalInputNormalizationResult NormalizeInstant(object value)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Utc
                ? TemporalInputNormalizationResult.Success(TruncateToSecond(dateTime))
                : NormalizeLocalDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified));
        }

        var text = ReadString(value);
        if (!DateTime.TryParseExact(
                text,
                "yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localTime))
        {
            return TemporalInputNormalizationResult.Failure(
                "TEMPORAL_INPUT_INVALID",
                "时间点必须使用 yyyy-MM-ddTHH:mm:ss 格式。");
        }

        return NormalizeLocalDateTime(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified));
    }

    private TemporalInputNormalizationResult NormalizeLocalDateTime(DateTime localTime)
    {
        var timeZone = ResolveTimeZone();
        if (timeZone.IsInvalidTime(localTime))
        {
            return TemporalInputNormalizationResult.Failure(
                "TEMPORAL_LOCAL_TIME_INVALID",
                "本地时间落在夏令时跳变的无效区间。请重新选择时间。");
        }

        if (timeZone.IsAmbiguousTime(localTime))
        {
            return TemporalInputNormalizationResult.Failure(
                "TEMPORAL_LOCAL_TIME_AMBIGUOUS",
                "本地时间因夏令时回拨存在歧义。请重新选择时间。");
        }

        return TemporalInputNormalizationResult.Success(
            TruncateToSecond(TimeZoneInfo.ConvertTimeToUtc(localTime, timeZone)));
    }

    private TimeZoneInfo ResolveTimeZone() =>
        TimeZoneInfo.FindSystemTimeZoneById(contextAccessor.Current?.TimeZoneId ?? defaultTimeZoneId);

    private static string? ReadString(object value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => null
    };

    private static DateTime TruncateToSecond(DateTime value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
}
