using System.Security.Claims;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace ICP.Platform.Authorization.Tests;
public sealed class PermissionGateTests
{
    [Fact]
    public async Task AddPlatformAuthorization_WhenFeatureSourceIsNotRegistered_FailsClosed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformAuthorization();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IFeatureDecisionSource>();
        Assert.Equal(PermissionDecision.Indeterminate, await source.EvaluateAsync("subject", "read", "Resource", "resource.read", default));
    }
    [Fact]
    public void FromPrincipal_RejectsAuthenticatedPrincipalWithoutTrustedSubjectId()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "test"));

        var exception = Assert.Throws<PermissionSubjectException>(() =>
            AuthenticatedSubject.FromPrincipal(principal));

        Assert.Equal(PermissionGateFailureCodes.SubjectMissing, exception.FailureCode);
    }

    private static ClaimsPrincipal CreatePrincipal(string subjectId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, subjectId)],
            "test"));

}
