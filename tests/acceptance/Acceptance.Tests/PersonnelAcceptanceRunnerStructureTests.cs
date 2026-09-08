using System.Text.Json;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class PersonnelAcceptanceRunnerStructureTests
{
    [Fact]
    public void TemporalBrowserInputs_UseTheLocalizedPersonFormLabels()
    {
        var repositoryRoot = GetRepositoryRoot();
        using var dictionary = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot,
            "modules",
            "Organization",
            "metadata",
            "dictionaries",
            "PersonListView.dic.json")));
        var personDictionary = dictionary.RootElement.GetProperty("PersonListView");
        var expected = new[]
        {
            ReadSimplifiedChineseLabel(personDictionary, "Field_BirthDate"),
            ReadSimplifiedChineseLabel(personDictionary, "Field_EmploymentStartedAt"),
            ReadSimplifiedChineseLabel(personDictionary, "Field_EmploymentEndedAt")
        };

        Assert.Equal(expected, PersonnelAcceptanceRunner.PersonnelTemporalFormInputs.Select(input => input.Label));
    }

    [Fact]
    public void TemporalBrowserInputs_BlurWithoutSubmittingTheEditForm()
    {
        Assert.Equal("Tab", PersonnelAcceptanceRunner.PersonnelTemporalInputExitKey);
    }

    [Fact]
    public void BrowserScenario_CreatesCompanyAndDepartmentBeforeAssigningThePersonnelAffiliation()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "PersonnelAcceptanceRunner.cs"));

        Assert.Contains(
            "await CreateCompanyFromBrowserAsync(page, cancellationToken);",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "await CreateDepartmentFromBrowserAsync(page, company, cancellationToken);",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "await UpdatePersonFromBrowserAsync(",
            runner,
            StringComparison.Ordinal);
        Assert.Contains("CompanyData company,", runner, StringComparison.Ordinal);
        Assert.Contains("DepartmentData department,", runner, StringComparison.Ordinal);
        Assert.Contains("Name = \"组织管理\"", runner, StringComparison.Ordinal);
        Assert.Contains("/api/organization/companies", runner, StringComparison.Ordinal);
        Assert.Contains("/organization/structure", runner, StringComparison.Ordinal);
        Assert.Contains("/api/organization/departments", runner, StringComparison.Ordinal);
        Assert.Contains("\"所属公司\"", runner, StringComparison.Ordinal);
        Assert.Contains("\"所属部门\"", runner, StringComparison.Ordinal);
        var departmentJourney = runner[
            runner.IndexOf("private async Task<DepartmentData> CreateDepartmentFromBrowserAsync", StringComparison.Ordinal)..
            runner.IndexOf("private async Task<PositionData> CreatePositionFromBrowserAsync", StringComparison.Ordinal)];
        Assert.DoesNotContain(
            "/api/organization/department-lookups/companies",
            departmentJourney,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/api/organization/person-lookups/companies",
            departmentJourney,
            StringComparison.Ordinal);
        Assert.Contains("Name = \"直属部门\"", departmentJourney, StringComparison.Ordinal);
        Assert.Contains("Name = \"基本信息\", Selected = true", departmentJourney, StringComparison.Ordinal);
        Assert.Contains("Button = MouseButton.Right", departmentJourney, StringComparison.Ordinal);
        Assert.Contains("AriaRole.Menuitem", departmentJourney, StringComparison.Ordinal);
        Assert.Contains("Name = \"保存修改\"", departmentJourney, StringComparison.Ordinal);
        Assert.Contains("tree.GetByText(updated.Name", departmentJourney, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".Filter(new LocatorFilterOptions { HasTextString = company.Name })",
            departmentJourney,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserScenario_ProvesAdministratorPermissionsAndReportsCapabilityErrors()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "PersonnelAcceptanceRunner.cs"));

        Assert.Contains("Organization.Company.Create", runner, StringComparison.Ordinal);
        Assert.Contains("Organization.Department.Create", runner, StringComparison.Ordinal);
        Assert.Contains("Organization.Person.Update", runner, StringComparison.Ordinal);
        Assert.Contains("[data-route-capability-error]", runner, StringComparison.Ordinal);
        Assert.Contains(".sh-error-state:visible", runner, StringComparison.Ordinal);
        Assert.Contains("Visible body:", runner, StringComparison.Ordinal);
        Assert.Contains(
            "Browser company create returned {response.Status}, expected 201: {body}",
            runner,
            StringComparison.Ordinal);
        var lookupJourney = runner[
            runner.IndexOf("private static async Task SelectOrganizationLookupAsync", StringComparison.Ordinal)..
            runner.IndexOf("private static async Task ClickStandardCrudCreateActionAsync", StringComparison.Ordinal)];
        Assert.DoesNotContain("await lookup.ClickAsync();", lookupJourney, StringComparison.Ordinal);
        Assert.Contains("await lookup.FillAsync(option);", lookupJourney, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserScenario_CoversPositionCreationAndPersonLookupAssignmentInThePersonnelGate()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "PersonnelAcceptanceRunner.cs"));

        Assert.Contains(
            "await CreatePositionFromBrowserAsync(page, cancellationToken);",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "await UpdatePersonFromBrowserAsync(",
            runner,
            StringComparison.Ordinal);
        Assert.Contains("OpenOrganizationTabAsync(page, \"岗位\", \"positions\")", runner, StringComparison.Ordinal);
        Assert.Contains("/api/organization/positions", runner, StringComparison.Ordinal);
        Assert.Contains(
            "new LocatorGetByRoleOptions { Name = \"保存并关闭\", Exact = true }",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "new LocatorGetByRoleOptions { Name = \"保存修改\", Exact = true }",
            runner,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new LocatorGetByRoleOptions { Name = \"保存\", Exact = true }",
            runner,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserScenario_RemainsOnePrimaryPersonnelJourney()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "PersonnelAcceptanceRunner.cs"));

        Assert.Contains("await AssertPersonBirthDateRangeAsync(page, person);", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureResponsiveBrowserEvidenceAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureRestrictedBrowserEvidenceAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureUnauthorizedDetailEvidenceAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureDetailContractDriftBrowserEvidenceAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("AssertFullDetailJourneyAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("AssertSubmittedQueryAndKeyboardAsync", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("AssertApiAuditCheckpointAfterRestartAsync", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_DoesNotRepeatGateEntrypointBuild()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "PersonnelAcceptanceRunner.cs"));

        Assert.DoesNotContain("preflight-build.json", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build HostApi", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapPreflight_ReportsUnexpectedStartupBeforeReadingDatabaseOwnership()
    {
        var runner = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "tests", "acceptance",
            "Acceptance.Runner", "PersonnelAcceptanceRunner.cs"));
        var bootstrap = runner[runner.IndexOf("private async Task ObserveBootstrapRequiredDevAsync", StringComparison.Ordinal)..];
        var startupFailure = bootstrap.IndexOf("Empty Personnel catalog startup returned", StringComparison.Ordinal);
        var ownershipRead = bootstrap.IndexOf("_ownershipSnapshot = await ReadOwnershipSnapshotAsync", StringComparison.Ordinal);
        Assert.True(startupFailure >= 0 && startupFailure < ownershipRead);
        Assert.Contains("diagnostic={Sanitize(diagnostic)}; devRun={_lastDevRunId}", bootstrap, StringComparison.Ordinal);
    }
    [Fact]
    public void PersonnelConsumers_UseTheManagementTabAndKeepTheLegacyAndFullDetailJourney()
    {
        var root = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(root, "tests", "acceptance", "Acceptance.Runner", "PersonnelAcceptanceRunner.cs"));
        Assert.Contains("/organization/persons/{person.Id}", runner, StringComparison.Ordinal);
        Assert.Contains("Name = \"查看完整详情\"", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("searchParams.get('keyword')", runner, StringComparison.Ordinal);
        var coldStart = File.ReadAllText(Path.Combine(root, "tests", "acceptance", "Acceptance.Runner", "ProductionColdStartBrowserRunner.cs"));
        Assert.Contains("/organization/structure?tab=persons", coldStart, StringComparison.Ordinal);
        Assert.Contains("Name = \"组织管理\"", coldStart, StringComparison.Ordinal);
        Assert.Contains("Name = \"人员\"", coldStart, StringComparison.Ordinal);
        Assert.DoesNotContain("Name = \"人员中心\"", coldStart, StringComparison.Ordinal);
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

    private static string ReadSimplifiedChineseLabel(JsonElement dictionary, string key) =>
        dictionary.GetProperty(key).GetProperty("zhs").GetString()
        ?? throw new InvalidOperationException($"人员表单字典缺少 {key}.zhs。");
}
