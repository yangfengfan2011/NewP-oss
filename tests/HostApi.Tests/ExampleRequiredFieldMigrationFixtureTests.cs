using ICP.Platform.HostApi.Acceptance;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ExampleRequiredFieldMigrationFixtureTests
{
    [Theory]
    [InlineData("Development", true, "expected-run")]
    [InlineData("Acceptance", false, "expected-run")]
    [InlineData("Acceptance", true, "different-run")]
    public async Task ApplyHistoricalStageAsync_WithoutExactAcceptanceOptIn_FailsBeforeDatabaseAccess(
        string environmentName,
        bool enabled,
        string configuredRunId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Acceptance:Enabled"] = enabled.ToString(),
                ["Acceptance:RunId"] = configuredRunId,
                ["ConnectionStrings:SqlServer"] = "not-used"
            })
            .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ExampleRequiredFieldMigrationFixture.ApplyHistoricalStageAsync(
                GetRepositoryRoot(),
                configuration,
                "expected-run",
                environmentName,
                stage: 2));

        Assert.StartsWith("ACC_EXAMPLE_FIXTURE_NOT_ENABLED", exception.Message);
    }

    [Fact]
    public void BuildHistoricalPlan_UsesCurrentExampleHistoryPrefixAndMatchingModel()
    {
        var repositoryRoot = GetRepositoryRoot();

        var beforeField = ExampleRequiredFieldMigrationFixture.BuildHistoricalPlan(repositoryRoot, stage: 2);
        var nullableField = ExampleRequiredFieldMigrationFixture.BuildHistoricalPlan(repositoryRoot, stage: 3);

        var beforeExample = Assert.Single(beforeField.Streams, stream => stream.ModuleId == "Example");
        var nullableExample = Assert.Single(nullableField.Streams, stream => stream.ModuleId == "Example");
        Assert.Equal(2, beforeExample.Migrations.Count);
        Assert.EndsWith("_Example_000002_ReviewerUserEID", beforeExample.ExpectedFrontier, StringComparison.Ordinal);
        Assert.Equal(3, nullableExample.Migrations.Count);
        Assert.EndsWith("_Example_000003_RecordSourceNullable", nullableExample.ExpectedFrontier, StringComparison.Ordinal);

        var beforeModel = Assert.Single(beforeField.Models, model => model.LogicalKey == "Example/ExampleRecord");
        var nullableModel = Assert.Single(nullableField.Models, model => model.LogicalKey == "Example/ExampleRecord");
        Assert.DoesNotContain(beforeModel.Fields, field => field.Name == "RecordSource");
        Assert.False(Assert.Single(nullableModel.Fields, field => field.Name == "RecordSource").Required);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
