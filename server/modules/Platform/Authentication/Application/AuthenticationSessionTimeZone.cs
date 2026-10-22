namespace ICP.Platform.Modules.Authentication.Application;

internal static class AuthenticationSessionTimeZone
{
    // 用 IANA 映射能力确认命名体系，再按原 IANA ID 验证当前部署的时区数据库，避免有损改写。
    public static bool TryNormalize(string? value, out string normalized)
    {
        var candidate = value?.Trim() ?? string.Empty;
        normalized = string.Empty;
        if (candidate.Length == 0
            || !TimeZoneInfo.TryConvertIanaIdToWindowsId(candidate, out _))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(candidate);
            normalized = candidate;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    // 显式输入优先；字段缺失或 null 时只使用已经通过启动验证的部署默认值。
    public static string Resolve(string? explicitTimeZoneId, string defaultTimeZoneId)
    {
        var candidate = explicitTimeZoneId ?? defaultTimeZoneId;
        if (!TryNormalize(candidate, out var normalized))
        {
            throw new ICP.Platform.Authentication.Contracts.AuthenticationSessionException(
                ICP.Platform.Authentication.Contracts.AuthenticationSessionFailureCodes.InvalidTimeZone);
        }

        return normalized;
    }
}
