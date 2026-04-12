namespace ICP.Platform.Modules.Organization.Services;

public sealed class NullIdentityUserDirectory : IIdentityUserDirectory
{
    public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
