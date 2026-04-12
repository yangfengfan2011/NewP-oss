namespace ICP.Platform.Modules.Organization.Services;

public sealed class NullAuthorizationSummaryReader : IAuthorizationSummaryReader
{
    public Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
