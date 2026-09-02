namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record SetPersonLifecycleStatusRequest(
    bool IsEnabled,
    string Reason);
