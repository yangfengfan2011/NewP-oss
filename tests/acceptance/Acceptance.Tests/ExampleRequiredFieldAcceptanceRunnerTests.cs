using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class ExampleRequiredFieldAcceptanceRunnerTests
{
    [Fact]
    public void RequiredScenarios_CoverMigrationFailuresBrowserSmokeAndRestart()
    {
        Assert.Equal(
            [
                "example-required-field.preflight",
                "example-required-field.migration",
                "example-required-field.backfill-recovery",
                "example-required-field.contract-precondition",
                "example-required-field.browser",
                "example-required-field.restart"
            ],
            ExampleRequiredFieldAcceptanceRunner.RequiredScenarios);
    }

    [Fact]
    public void AdministratorPassword_IsOpaqueAndUniquePerGeneration()
    {
        var first = ExampleRequiredFieldAcceptanceRunner.CreateAdministratorPassword();
        var second = ExampleRequiredFieldAcceptanceRunner.CreateAdministratorPassword();

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("acceptance-run", first, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
    }
}
