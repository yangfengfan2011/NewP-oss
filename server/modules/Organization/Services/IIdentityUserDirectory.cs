namespace ICP.Platform.Modules.Organization.Services;

public interface IIdentityUserDirectory
{
    Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default);
}
