using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class FormalIdentityBootstrapTests
{
    [Fact]
    public void ResolveHostWorkingDirectory_UsesHostProjectRoot()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "newp-repository");

        var workingDirectory = FormalIdentityBootstrap.ResolveHostWorkingDirectory(repositoryRoot);

        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, "apps", "host-api")), workingDirectory);
    }

    [Fact]
    public void CreateFailureMessage_RedactsSecretsAndKeepsBoundedDiagnosticTail()
    {
        var output = "\u001b[31mstartup\u001b[0m\nPassword=database-secret;" + new string('x', 9_000)
            + "\nTemporary credential: temporary-secret\nSystem.InvalidOperationException: temporal failure";

        var message = FormalIdentityBootstrap.CreateFailureMessage(-1, 1, output);

        Assert.Contains("exit code -1 and 1 credential results", message, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: temporal failure", message, StringComparison.Ordinal);
        Assert.Contains("Temporary credential: [REDACTED]", message, StringComparison.Ordinal);
        Assert.DoesNotContain("database-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("temporary-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[31m", message, StringComparison.Ordinal);
        Assert.True(message.Length < 8_500, $"Diagnostic length was {message.Length}.");
    }

    [Fact]
    public void CreateFailureMessage_WithoutConsoleOutput_ReturnsStableSummary()
    {
        var message = FormalIdentityBootstrap.CreateFailureMessage(2, 0, " \r\n ");

        Assert.Equal(
            "Formal identity bootstrap failed with exit code 2 and 0 credential results.",
            message);
    }
}
