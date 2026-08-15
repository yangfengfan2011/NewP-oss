using System.Text.Json;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class RepositoryTestEntrypointTests
{
    [Fact]
    public void Solution_CoversEveryDiscoveredBackendTestProject()
    {
        var repositoryRoot = GetRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(repositoryRoot, "ICP-Platform.sln"))
            .Replace('\\', '/');
        var testProjects = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "tests"), "*.Tests.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(testProjects);
        foreach (var project in testProjects)
        {
            Assert.Contains(
                $"\"{project}\"",
                solution,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Solution_IncludesModelDtoSyncTool()
    {
        var solution = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "ICP-Platform.sln"))
            .Replace('\\', '/');

        Assert.Contains(
            "\"server/tools/model-dto-sync/ModelDtoSync.csproj\"",
            solution,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryTestScripts_InvokeTheSameSolutionAndFrontendEntrypoints()
    {
        var repositoryRoot = GetRepositoryRoot();
        var powerShell = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "test.ps1"));
        var shell = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "test.sh"));

        Assert.Contains("dotnet test ICP-Platform.sln", powerShell, StringComparison.Ordinal);
        Assert.Contains("dotnet test ICP-Platform.sln", shell, StringComparison.Ordinal);
        Assert.Contains("--maxcpucount:1", powerShell, StringComparison.Ordinal);
        Assert.Contains("corepack pnpm --dir frontend test", powerShell, StringComparison.Ordinal);
        Assert.Contains("corepack pnpm --dir frontend test", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendRootTestScript_RunsEveryWorkspaceThatDeclaresTests()
    {
        var repositoryRoot = GetRepositoryRoot();
        using var packageJson = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "frontend", "package.json")));

        var scripts = packageJson.RootElement.GetProperty("scripts");

        Assert.Equal("pnpm -r --if-present test", scripts.GetProperty("test").GetString());
    }

    [Fact]
    public void ContinuousIntegration_RunsFrontendTestsSeparatelyFromBuild()
    {
        var repositoryRoot = GetRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        Assert.Contains("pnpm test", workflow, StringComparison.Ordinal);
        Assert.Contains("pnpm build", workflow, StringComparison.Ordinal);
        Assert.Contains("validate-dic-localization.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("model-dto-sync/ModelDtoSync.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("--check .", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalVerification_BuildsOnceThenUsesNoBuildChecksAndWritesResultManifest()
    {
        var repositoryRoot = GetRepositoryRoot();
        var verification = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "verify.ps1"));
        var acceptance = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "accept.ps1"));

        Assert.Contains("\"build\", \"ICP-Platform.sln\", \"--configuration\", \"Release\", \"--no-restore\"", verification, StringComparison.Ordinal);
        Assert.Contains("\"test\", \"ICP-Platform.sln\", \"--configuration\", \"Release\", \"--no-build\", \"--no-restore\"", verification, StringComparison.Ordinal);
        Assert.Contains("\"--check\", $repoRoot", verification, StringComparison.Ordinal);
        Assert.Contains("\"-NoBuild\"", verification, StringComparison.Ordinal);
        Assert.Contains("current-result.json", verification, StringComparison.Ordinal);
        Assert.Contains("ACCEPT_GATE_REQUIRED", acceptance, StringComparison.Ordinal);
        Assert.DoesNotContain("[string]$Gate = \"All\"", acceptance, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptanceRunner_DoesNotReferenceInternalPersistenceImplementation()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runnerRoot = Path.Combine(repositoryRoot, "tests", "acceptance", "Acceptance.Runner");
        var project = File.ReadAllText(Path.Combine(runnerRoot, "Acceptance.Runner.csproj"))
            .Replace('\\', '/');

        Assert.DoesNotContain("server/platform-persistence", project, StringComparison.OrdinalIgnoreCase);
        foreach (var sourcePath in Directory.EnumerateFiles(runnerRoot, "*.cs", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(
                "ICP.Platform.Persistence",
                File.ReadAllText(sourcePath),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PersonnelAcceptanceRunner_PerformsExplicitBrowserHardRefresh()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "PersonnelAcceptanceRunner.cs"));

        Assert.Contains("ReloadAsync", runner, StringComparison.Ordinal);
        Assert.Contains("Name = person.Name", runner, StringComparison.Ordinal);
        Assert.Contains("person.PersonNo, person.Name, fixture.CompanyName", runner, StringComparison.Ordinal);
        Assert.Contains("WaitForPostLoginNavigationAsync(page)", runner, StringComparison.Ordinal);
        Assert.Contains("Name = \"人员中心\", Exact = true", runner, StringComparison.Ordinal);
        Assert.Contains("SubmitPersonnelKeywordAsync(page, person.PersonNo)", runner, StringComparison.Ordinal);
        Assert.Contains("Locator(\"button:visible\"", runner, StringComparison.Ordinal);
        Assert.Contains("Locator(\"tr:visible\"", runner, StringComparison.Ordinal);
        Assert.Contains("Filter(new LocatorFilterOptions { HasTextString = fixture.CompanyName })", runner, StringComparison.Ordinal);
        Assert.Contains("Locator(\".ant-drawer-content:visible\"", runner, StringComparison.Ordinal);
        Assert.Contains("Locator(\".ant-table-content:visible\"", runner, StringComparison.Ordinal);
        Assert.Contains("Name = \"刷新\", Exact = true", runner, StringComparison.Ordinal);
        Assert.Contains("Name = \"重置\", Exact = true", runner, StringComparison.Ordinal);
        Assert.Contains("await refresh.First.ClickAsync()", runner, StringComparison.Ordinal);
        Assert.Contains("await reset.First.ClickAsync()", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("Name = \"查看\"", runner, StringComparison.Ordinal);
        Assert.DoesNotContain(".ant-drawer-content #PersonNo", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("window.location.pathname !== '/login'", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("page.GotoAsync($\"http://127.0.0.1:{_shellPort}/organization/persons\"", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void PersonnelAcceptanceRunner_DeclaresIssue57AutomationMatrix()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "PersonnelAcceptanceRunner.cs"));

        foreach (var requiredCheck in new[]
        {
            "page-and-query",
            "toolbar-and-preferences",
            "crud-and-actions",
            "permissions-and-security",
            "responsive-and-accessibility",
            "browser-tablet.png",
            "browser-narrow.png",
            "browser-restricted.png"
        })
        {
            Assert.Contains(requiredCheck, runner, StringComparison.Ordinal);
        }

        Assert.Contains("listQuery[\"drawer\"], \"view\"", runner, StringComparison.Ordinal);
        Assert.Contains("listQuery[\"id\"], person.Id", runner, StringComparison.Ordinal);
        Assert.Contains("restoredDrawer.WaitForAsync", runner, StringComparison.Ordinal);
        Assert.Contains("restoredDrawer.GetByText(person.Name", runner, StringComparison.Ordinal);
        Assert.Contains("GetByRole(AriaRole.Tooltip)", runner, StringComparison.Ordinal);
        Assert.Contains("HasTextString = \"确认停用当前人员吗？\"", runner, StringComparison.Ordinal);
        Assert.Contains("PageGetByRoleOptions { NameRegex = new Regex(\"^确\\\\s*认$\") }", runner, StringComparison.Ordinal);
        Assert.Contains("LocatorGetByRoleOptions { NameRegex = new Regex(\"^取\\\\s*消$\") }", runner, StringComparison.Ordinal);
        Assert.Contains("Name = \"退出登录\", Exact = true", runner, StringComparison.Ordinal);
        Assert.Contains("requestedCapabilityUrls", runner, StringComparison.Ordinal);
        Assert.Contains("personStandardCrudResource", runner, StringComparison.Ordinal);
        Assert.Contains("browser-detail-unauthorized.json", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("url.Contains(\"/frontend/modules/organization/\"", runner, StringComparison.Ordinal);
        Assert.DoesNotContain(".ant-popover", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void PersonnelAcceptanceRunner_DeclaresIssue68DetailJourney()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "PersonnelAcceptanceRunner.cs"));

        var browserScenarioStart = runner.IndexOf(
            "private async Task RunBrowserScenarioAsync",
            StringComparison.Ordinal);
        var browserScenarioEnd = runner.IndexOf(
            "private async Task AssertSubmittedQueryAndKeyboardAsync",
            browserScenarioStart,
            StringComparison.Ordinal);
        Assert.True(browserScenarioStart >= 0 && browserScenarioEnd > browserScenarioStart);
        var browserScenario = runner[browserScenarioStart..browserScenarioEnd];

        foreach (var requiredCheck in new[]
        {
            "drawer-to-full-detail",
            "full-detail-direct-refresh-back-forward",
            "detail-three-sections-four-audit-fields",
            "detail-summary-ready",
            "detail-edit-permission",
            "detail-disable-required-reason",
            "detail-disable-success",
            "detail-disable-conflict",
            "detail-capability-denied-before-download"
        })
        {
            Assert.Contains(requiredCheck, browserScenario, StringComparison.Ordinal);
        }

        foreach (var apiOnlyCheck in new[]
        {
            "detail-disable-business-failure",
            "detail-disable-unauthorized",
            "detail-disable-forbidden"
        })
        {
            Assert.DoesNotContain(apiOnlyCheck, browserScenario, StringComparison.Ordinal);
            Assert.Contains(apiOnlyCheck, runner, StringComparison.Ordinal);
        }

        Assert.Contains("apiAssertions = checks", runner, StringComparison.Ordinal);
        Assert.Contains("browserAssertions = browserAssertions", runner, StringComparison.Ordinal);
        Assert.Contains("unitAssertions = new[]", runner, StringComparison.Ordinal);
        Assert.Contains("GetByText(\"已加载\", new LocatorGetByTextOptions { Exact = true }", runner, StringComparison.Ordinal);
        Assert.Contains("GetByText(\"已永久绑定\", new LocatorGetByTextOptions { Exact = true }", runner, StringComparison.Ordinal);
        Assert.Contains("Name = \"停用\", Exact = true", runner, StringComparison.Ordinal);

        var summaryUnit = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "packages",
            "shell",
            "src",
            "__tests__",
            "organization-person-center-detail-layout.test.tsx"));
        foreach (var summaryStatus in new[] { "status: 'ready'", "status: 'notConfigured'", "status: 'dependencyUnavailable'" })
        {
            Assert.Contains(summaryStatus, summaryUnit, StringComparison.Ordinal);
        }

        var lifecycleClientUnit = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "modules",
            "organization",
            "src",
            "__tests__",
            "personLifecycleClient.test.ts"));
        foreach (var responseStatus in new[] { "[400,", "[401,", "[403," })
        {
            Assert.Contains(responseStatus, lifecycleClientUnit, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PersonnelAcceptanceRunner_UsesFormalIdentityChainOnly()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runnerRoot = Path.Combine(repositoryRoot, "tests", "acceptance", "Acceptance.Runner");
        var runner = File.ReadAllText(Path.Combine(runnerRoot, "PersonnelAcceptanceRunner.cs"));
        var bootstrap = File.ReadAllText(Path.Combine(runnerRoot, "FormalIdentityBootstrap.cs"));

        Assert.Contains("FormalIdentityBootstrap.RunAsync", runner, StringComparison.Ordinal);
        Assert.Contains("bootstrap-local-admin", bootstrap, StringComparison.Ordinal);
        Assert.Contains("\"-HostEnvironmentName\", \"Acceptance\"", runner, StringComparison.Ordinal);
        Assert.Contains("CaptureAuthenticationCookie(client, response)", runner, StringComparison.Ordinal);
        Assert.Contains("new { ecode, password }", runner, StringComparison.Ordinal);
        Assert.Contains("#ecode", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalAuth", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("read-only-login", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("username", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#username", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("UseSharedDevelopmentCredential", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityFoundationAcceptanceRunner_UsesOnlyFormalBootstrapAndApplicationSurfaces()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "IdentityFoundationAcceptanceRunner.cs"));

        Assert.Contains("bootstrap-local-admin", runner, StringComparison.Ordinal);
        Assert.Contains("/api/identity/accounts", runner, StringComparison.Ordinal);
        Assert.Contains("/api/organization/persons", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/acceptance", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IdentityFoundationAcceptanceRunner_ExecutesRequiredBuiltinReadinessFixturesBeforeRecordingSecurityEvidence()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runner = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "acceptance",
            "Acceptance.Runner",
            "IdentityFoundationAcceptanceRunner.cs"));

        Assert.Contains("FullyQualifiedName~RequiredBuiltinReadinessTests", runner, StringComparison.Ordinal);
        Assert.Contains("requiredBuiltinsTests", runner, StringComparison.Ordinal);
        Assert.Contains("ProcessSummary", runner, StringComparison.Ordinal);
        Assert.True(
            runner.IndexOf("FullyQualifiedName~RequiredBuiltinReadinessTests", StringComparison.Ordinal)
            < runner.IndexOf("RecordScenarioSuccessAsync(SecurityScenario", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityFoundationStartupDtos_IncludePersonLifecycleRevision()
    {
        var repositoryRoot = GetRepositoryRoot();
        var personDto = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Organization",
            "DTOs",
            "Generated",
            "PersonDto.g.cs"));

        Assert.Contains("public long Revision { get; init; }", personDto, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionColdStartConfiguration_UsesFrozenCandidateBudgets()
    {
        var repositoryRoot = GetRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "performance",
            "production-cold-start.config.json")));
        var budgets = document.RootElement.GetProperty("budgets");

        AssertBudget(budgets, "anonymous-login", 1000, 1500, null);
        AssertBudget(budgets, "authenticated-home", 3700, 3900, 0.30m);
        AssertBudget(budgets, "authenticated-personnel", 6200, 6500, null);
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

    private static void AssertBudget(
        JsonElement budgets,
        string scenario,
        int expectedP50,
        int expectedP95,
        decimal? expectedP50Improvement)
    {
        var budget = budgets.GetProperty(scenario);
        Assert.Equal(expectedP50, budget.GetProperty("maxP50Ms").GetInt32());
        Assert.Equal(expectedP95, budget.GetProperty("maxP95Ms").GetInt32());

        Assert.False(budget.TryGetProperty("minimumImprovementRatio", out _));
        if (expectedP50Improvement is null)
        {
            Assert.False(budget.TryGetProperty("minimumP50ImprovementRatio", out _));
            return;
        }

        Assert.Equal(
            expectedP50Improvement,
            budget.GetProperty("minimumP50ImprovementRatio").GetDecimal());
    }
}
