using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

public sealed record PersonLifecycleActor(string ActorId, string TraceId);

public interface IPersonLifecycleService
{
    ValueTask<PersonLifecycleDto> SetStatusAsync(
        string personEid,
        SetPersonLifecycleStatusRequest request,
        PersonLifecycleActor actor,
        CancellationToken cancellationToken = default);
}
