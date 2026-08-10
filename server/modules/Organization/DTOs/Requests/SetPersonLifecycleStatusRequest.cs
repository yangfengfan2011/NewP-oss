namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record SetPersonLifecycleStatusRequest(
    bool IsEnabled,
    long ExpectedRevision,
    string Reason);
