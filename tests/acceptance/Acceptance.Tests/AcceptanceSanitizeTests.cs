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
}
