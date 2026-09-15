namespace ICP.Platform.Authorization;

/// <summary>
/// 功能判权对 HTTP 与日志边界公开的稳定失败码。
/// </summary>
public static class PermissionGateFailureCodes
{
    public const string PermissionDenied = "Authorization.PermissionDenied";
    public const string DecisionUnavailable = "Authorization.DecisionUnavailable";
    public const string SubjectMissing = "Authorization.SubjectMissing";
}
