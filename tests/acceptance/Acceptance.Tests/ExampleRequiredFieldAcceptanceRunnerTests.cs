using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class ExampleRequiredFieldAcceptanceRunnerTests
{
    [Fact]
    public void StableDiagnostic_ExtractsFailureCodesWithoutPathsOrCredentials()
    {
        const string log = """
            Unhandled exception. ICP.Platform.PluginPlatform.Activation.PluginActivationException: Plugin activation failed (PLUGIN_ACTIVATION_MIGRATION_FAILED).
             ---> ICP.Platform.Persistence.Ownership.DatabaseOwnershipException: Database ownership validation failed (PERSISTENCE_MIGRATION_HISTORY_NON_PREFIX).
               at SqlServerDatabaseOwnershipGuard.VerifyAsync() in D:\Work\Git\NewP\server\Guard.cs:line 70
            Server=localhost;User ID=sa;Password=NotASecretHere
            """;

        var actual = ExampleRequiredFieldAcceptanceRunner.ExtractStableDiagnostic(log);

        Assert.Equal(
            "PLUGIN_ACTIVATION_MIGRATION_FAILED / PERSISTENCE_MIGRATION_HISTORY_NON_PREFIX",
            actual);
        Assert.DoesNotContain("D:\\Work", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void StableDiagnostic_WhenLogHasNoFailureCode_ReturnsEmptyInsteadOfLogText()
    {
        const string log = "info: Microsoft.EntityFrameworkCore.Database.Command[20101] Executed DbCommand SELECT 1";

        Assert.Equal(string.Empty, ExampleRequiredFieldAcceptanceRunner.ExtractStableDiagnostic(log));
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
