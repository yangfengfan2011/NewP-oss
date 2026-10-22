using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class IdentitySmokeExecutionEvidenceTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(2, 1, 1, 1)]
    [InlineData(2, 2, 1, 0)]
    public void RejectsEmptySkippedOrFailedSmokeRuns(int total, int executed, int passed, int skipped) =>
        Assert.Throws<InvalidOperationException>(() => IdentityFoundationAcceptanceRunner.RequireExecutedSmokeTests(
            Trx(total, executed, passed, skipped)));

    [Fact]
    public void AcceptsOnlyFullyExecutedPassingSmokeRuns() =>
        IdentityFoundationAcceptanceRunner.RequireExecutedSmokeTests(Trx(3, 3, 3, 0));

    private static string Trx(int total, int executed, int passed, int skipped) =>
        $"<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><ResultSummary><Counters total=\"{total}\" executed=\"{executed}\" passed=\"{passed}\" notExecuted=\"{skipped}\" /></ResultSummary></TestRun>";
}
