namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PersonLifecycleAccountDto(
    string AccountEid,
    string Status,
    long Revision,
    bool PersonnelDisableHold);

public sealed record PersonLifecycleDto(
    string PersonEid,
    bool IsEnabled,
    string? AccountEid,
    PersonLifecycleAccountDto? Account);
