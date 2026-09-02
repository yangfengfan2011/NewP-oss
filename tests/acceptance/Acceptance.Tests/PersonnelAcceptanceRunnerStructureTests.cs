using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class PersonnelAcceptanceRunnerStructureTests
{
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
}
