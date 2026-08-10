namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record BindPersonUserRequest(
    string UserEid,
    long ExpectedRevision,
    string Reason);
