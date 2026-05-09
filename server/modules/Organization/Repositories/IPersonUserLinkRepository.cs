using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Repositories;

public interface IPersonUserLinkRepository
{
    Task<PersonUserLink?> GetByPersonIdAsync(string personId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByUserIdAsync(
        string userId,
        string? excludedPersonId = null,
        CancellationToken cancellationToken = default);
    Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default);
}
