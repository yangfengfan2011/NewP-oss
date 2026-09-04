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

        var beforeOrganization = Assert.Single(beforeField.Streams, stream => stream.ModuleId == "Organization");
        var nullableOrganization = Assert.Single(nullableField.Streams, stream => stream.ModuleId == "Organization");
        Assert.Single(beforeOrganization.Migrations);
        Assert.Single(nullableOrganization.Migrations);
        Assert.EndsWith("_Organization_000001_Foundation", beforeOrganization.ExpectedFrontier, StringComparison.Ordinal);
        Assert.EndsWith("_Organization_000001_Foundation", nullableOrganization.ExpectedFrontier, StringComparison.Ordinal);
        Assert.Equal(beforeOrganization.Migrations[0].ToModelDigest, beforeOrganization.ModelDigest);
        Assert.Equal(beforeOrganization.ModelDigest, nullableOrganization.ModelDigest);

        var beforeModel = Assert.Single(beforeField.Models, model => model.LogicalKey == "Example/ExampleRecord");
        var nullableModel = Assert.Single(nullableField.Models, model => model.LogicalKey == "Example/ExampleRecord");
        Assert.DoesNotContain(beforeModel.Fields, field => field.Name == "RecordSource");
        Assert.False(Assert.Single(nullableModel.Fields, field => field.Name == "RecordSource").Required);

        var beforePosition = Assert.Single(beforeField.Models, model => model.LogicalKey == "Organization/Position");
        Assert.Contains(beforePosition.Fields, field => field.Name == "CompanyId" && field.Required);
        Assert.Contains(beforePosition.Fields, field => field.Name == "DepartmentId" && field.Required);
        Assert.Contains(beforePosition.Fields, field => field.Name == "ECODE" && field.Required);
        Assert.DoesNotContain(beforePosition.Fields, field => field.Name == "Description");

        var beforeCompany = Assert.Single(beforeField.Models, model => model.LogicalKey == "Organization/Company");
        Assert.Contains(beforeCompany.Indexes, index =>
            index.Name == "UX_Company_ECODE"
            && index.IsUnique
            && index.Fields.SequenceEqual(["ECODE"]));
        Assert.DoesNotContain(beforeCompany.Indexes, index => index.Name == "UX_Company_ENAME");
        Assert.Empty(beforeCompany.ForeignKeys);

        var beforeDepartment = Assert.Single(
            beforeField.Models,
            model => model.LogicalKey == "Organization/Department");
        Assert.Contains(beforeDepartment.Indexes, index =>
            index.Name == "IX_Department_Company_Parent_ECODE"
            && !index.IsUnique
            && index.Fields.SequenceEqual(["CompanyId", "ParentDepartmentId", "ECODE"]));
        Assert.DoesNotContain(beforeDepartment.Indexes, index =>
            index.Name is "UX_Department_Company_ECODE" or "UX_Department_Company_Parent_ENAME");
        Assert.Empty(beforeDepartment.ForeignKeys);
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
