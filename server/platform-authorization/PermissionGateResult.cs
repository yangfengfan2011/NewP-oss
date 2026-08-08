namespace ICP.Platform.Authorization;

/// <summary>
/// PermissionGate 的稳定结果；Granted 没有失败码，其余状态必须携带稳定失败码。
/// </summary>
public sealed record PermissionGateResult(
    PermissionDecision Decision,
    string? FailureCode);
