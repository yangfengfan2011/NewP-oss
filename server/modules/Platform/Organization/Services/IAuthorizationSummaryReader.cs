namespace ICP.Platform.Modules.Organization.Services;

public interface IAuthorizationSummaryReader
{
    Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken cancellationToken = default);
}
