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
            "await AssignPositionToPersonFromBrowserAsync(page, person, position, cancellationToken);",
            runner,
            StringComparison.Ordinal);
        Assert.Contains("/organization/positions", runner, StringComparison.Ordinal);
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
