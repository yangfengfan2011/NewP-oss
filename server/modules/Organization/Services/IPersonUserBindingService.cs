using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

public sealed record PersonUserBindingActor(string ActorId, string TraceId);

public interface IPersonUserBindingService
{
    ValueTask<IReadOnlyList<PersonUserBindingCandidateDto>> SearchCandidatesAsync(
        string personEid,
        string? query,
        CancellationToken cancellationToken = default);

    ValueTask<PersonUserBindingDto> BindAsync(
        string personEid,
        BindPersonUserRequest request,
        PersonUserBindingActor actor,
        CancellationToken cancellationToken = default);
}
