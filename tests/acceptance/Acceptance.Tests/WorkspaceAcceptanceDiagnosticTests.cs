using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class WorkspaceAcceptanceDiagnosticTests
{
    [Fact]
    public async Task UnavailableBody_PreservesRequestMethodUrlStatusAndReadFailure()
    {
        var diagnostic = await WorkspaceAcceptanceRunner.ReadFailedResponseDiagnosticAsync("GET",
            "http://127.0.0.1:3000/api/workspace/spaces", 500,
            () => Task.FromException<string>(new InvalidOperationException("No resource with given identifier found")));

        Assert.Contains("GET http://127.0.0.1:3000/api/workspace/spaces: 500", diagnostic);
        Assert.Contains("Response body unavailable: No resource with given identifier found", diagnostic);
    }

    [Fact]
    public async Task AvailableBody_PreservesRequestIdentityAndServerDiagnostic()
    {
        var diagnostic = await WorkspaceAcceptanceRunner.ReadFailedResponseDiagnosticAsync("POST",
            "http://127.0.0.1:3000/api/workspace/content/folders", 409, () => Task.FromResult("duplicate folder"));

        Assert.Equal("POST http://127.0.0.1:3000/api/workspace/content/folders: 409 duplicate folder", diagnostic);
    }
}
