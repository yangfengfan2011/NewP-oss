using System.Security.Claims;

namespace ICP.Platform.Authorization;

/// <summary>
/// 只能从服务器认证建立的 ClaimsPrincipal 提取的可信主体。
/// </summary>
public sealed record AuthenticatedSubject
{
    private AuthenticatedSubject(string subjectId)
    {
        SubjectId = subjectId;
    }

    public string SubjectId { get; }

    /// <summary>
    /// 从已认证 principal 的唯一 NameIdentifier claim 建立可信主体。
    /// </summary>
    public static AuthenticatedSubject FromPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new PermissionSubjectException(PermissionGateFailureCodes.SubjectMissing);
        }

        var subjectIds = principal.FindAll(ClaimTypes.NameIdentifier)
            .Select(static claim => claim.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (subjectIds.Length != 1)
        {
            throw new PermissionSubjectException(PermissionGateFailureCodes.SubjectMissing);
        }

        return new AuthenticatedSubject(subjectIds[0]);
    }
}

/// <summary>
/// 表示认证边界无法建立唯一可信主体。
/// </summary>
public sealed class PermissionSubjectException : InvalidOperationException
{
    public PermissionSubjectException(string failureCode)
        : base($"Authenticated subject validation failed ({failureCode}).")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}
