using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class AcceptanceSanitizeTests
{
    [Fact]
    public void Sanitize_ReplacesLongestTokenFirstSoShortTokensLeaveNoFragment()
    {
        // 密码同时是完整连接串的子串；若先替换短 token，连接串会只剩下未被整体替换的残片。
        const string password = "P@ssw0rd";
        const string connection = "Server=localhost;Database=NewP;User ID=sa;Password=REDACTED_FOR_OPEN_SOURCE";
        string[] tokens = [password, connection];

        var actual = AcceptanceInfrastructure.Sanitize(tokens, $"failed to open {connection}");

        Assert.Equal("failed to open [REDACTED]", actual);
        Assert.DoesNotContain("Server=localhost", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(password, actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_SkipsBlankTokensInsteadOfRedactingOrdinaryText()
    {
        string[] tokens = ["", "   ", "secret"];

        var actual = AcceptanceInfrastructure.Sanitize(tokens, "host reported secret failure");

        Assert.Equal("host reported [REDACTED] failure", actual);
    }

    [Fact]
    public void Sanitize_TreatsNullValueAsEmptyInsteadOfThrowing()
    {
        Assert.Equal(string.Empty, AcceptanceInfrastructure.Sanitize(["secret"], null));
    }

    [Fact]
    public void Sanitize_LeavesTextWithoutKnownTokensUnchanged()
    {
        const string diagnostic = "PERSISTENCE_MIGRATION_HISTORY_NON_PREFIX";

        Assert.Equal(diagnostic, AcceptanceInfrastructure.Sanitize(["secret"], diagnostic));
    }

    [Fact]
    public void Sanitize_IgnoresTokensBelowMinimumLengthSoOrdinaryWordsSurvive()
    {
        // 登录名之类的短值不是凭据，却会把含该子串的正常词打成 [REDACTED]。
        var actual = AcceptanceInfrastructure.Sanitize(["sa"], "sanitized evidence");

        Assert.Equal("sanitized evidence", actual);
    }

    [Fact]
    public void AddConnectionSecrets_RegistersBarePasswordAndBothPrefixedForms()
    {
        var tokens = new List<string>();

        AcceptanceInfrastructure.AddConnectionSecrets(tokens, "Server=localhost", "P@ssw0rd");

        Assert.Contains("Server=localhost", tokens);
        Assert.Contains("P@ssw0rd", tokens);
        Assert.Contains("Password=P@ssw0rd", tokens);
        Assert.Contains("Pwd=P@ssw0rd", tokens);
    }

    [Fact]
    public void AddConnectionSecrets_WithoutPasswordRegistersOnlyTheConnectionString()
    {
        var tokens = new List<string>();

        AcceptanceInfrastructure.AddConnectionSecrets(tokens, "Server=localhost", null);

        Assert.Equal(["Server=localhost"], tokens);
    }

    [Fact]
    public async Task ScanForSecrets_DetectsTokenWrittenIntoEvidenceAndIgnoresCleanRun()
    {
        var runDirectory = Directory.CreateTempSubdirectory("newp-scan-").FullName;
        try
        {
            var evidencePath = Path.Combine(runDirectory, "evidence.json");
            await File.WriteAllTextAsync(evidencePath, """{"diagnostic":"host reported P@ssw0rd"}""");

            Assert.True(await AcceptanceInfrastructure.ScanForSecretsAsync(
                runDirectory,
                ["P@ssw0rd"],
                CancellationToken.None));

            await File.WriteAllTextAsync(evidencePath, """{"diagnostic":"host reported [REDACTED]"}""");

            Assert.False(await AcceptanceInfrastructure.ScanForSecretsAsync(
                runDirectory,
                ["P@ssw0rd"],
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(runDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ScanForSecrets_UsesTheSameMinimumLengthAsSanitize()
    {
        var runDirectory = Directory.CreateTempSubdirectory("newp-scan-floor-").FullName;
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(runDirectory, "evidence.json"),
                """{"diagnostic":"sanitized evidence"}""");

            // "sa" 低于下限：脱敏跳过它，扫描也不得因此报泄漏，否则两端判据不一致。
            Assert.False(await AcceptanceInfrastructure.ScanForSecretsAsync(
                runDirectory,
                ["sa"],
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(runDirectory, recursive: true);
        }
    }
}
