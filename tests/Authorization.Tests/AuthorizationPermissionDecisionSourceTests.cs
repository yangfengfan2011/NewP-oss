using ICP.Platform.Authorization;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class AuthorizationPermissionDecisionSourceTests
{
    [Theory]
    [InlineData(true, PermissionDecision.Granted)]
    [InlineData(false, PermissionDecision.Denied)]
    public async Task EvaluateAsync_UsesCurrentAuthorizationStoreResult(
        bool allowed,
        PermissionDecision expected)
    {
        var authorization = new RecordingAuthorizationService(allowed);
        var source = new AuthorizationPermissionDecisionSource(authorization);

        var decision = await source.EvaluateAsync(
            "current-subject",
            "Organization.Person.View");

        Assert.Equal(expected, decision);
        Assert.Equal("current-subject", authorization.LastSubjectId);
        Assert.Equal("Organization.Person.View", authorization.LastPermissionCode);
    }

    private sealed class RecordingAuthorizationService : IAuthorizationService
    {
        private readonly bool _allowed;

        public RecordingAuthorizationService(bool allowed)
        {
            _allowed = allowed;
        }

        public string? LastSubjectId { get; private set; }

        public string? LastPermissionCode { get; private set; }

        public Task<bool> HasPermissionAsync(
            string userId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            LastSubjectId = userId;
            LastPermissionCode = permissionCode;
            return Task.FromResult(_allowed);
        }

        public Task<bool> HasAnyPermissionAsync(
            string userId,
            IEnumerable<string> permissionCodes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasAllPermissionsAsync(
            string userId,
            IEnumerable<string> permissionCodes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<List<string>> GetUserPermissionsAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<List<string>> GetUserRolesAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
