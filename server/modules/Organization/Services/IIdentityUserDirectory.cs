using ICP.Platform.Modules.Organization.DTOs;

namespace ICP.Platform.Modules.Organization.Services;

public interface IIdentityUserDirectory
{
    Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(string keyword, CancellationToken cancellationToken = default);
}
