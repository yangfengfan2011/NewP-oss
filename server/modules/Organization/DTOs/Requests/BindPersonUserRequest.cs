using System.ComponentModel.DataAnnotations;

namespace ICP.Platform.Modules.Organization.DTOs.Requests;

public sealed record BindPersonUserRequest(
    string UserEid,
    [Range(0, long.MaxValue)] long ExpectedRevision,
    string Reason);
