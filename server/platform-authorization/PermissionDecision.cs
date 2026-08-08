namespace ICP.Platform.Authorization;

/// <summary>
/// PermissionGate 的三态授权决策。
/// </summary>
public enum PermissionDecision
{
    Granted,
    Denied,
    Indeterminate
}
