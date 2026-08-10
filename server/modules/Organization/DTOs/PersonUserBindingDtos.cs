namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PersonUserBindingCandidateDto(
    string UserEid,
    string ECODE,
    string ENAME,
    string Status,
    long Revision);

public sealed record PersonUserBindingAccountDto(
    string UserEid,
    string ECODE,
    string ENAME,
    string Status,
    long Revision);

public sealed record PersonUserBindingDto(
    string PersonEid,
    string UserEid,
    DateTime BoundAt,
    string BoundBy,
    string Reason,
    PersonUserBindingAccountDto? Account);

public sealed record PersonUserBindingSummaryDto(
    string PersonEid,
    string PersonName,
    bool IsPersonEnabled,
    string? UserEid,
    DateTime? BoundAt,
    string? BoundBy,
    string? Reason,
    PersonUserBindingAccountDto? Account,
    bool IsAccountAvailable);
