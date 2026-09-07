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

    // 此处检查入口声明；两种模式的实际执行和失败截断由 VerificationPipelineTests 覆盖。
    [Fact]
    public void VerificationEntrypoint_DeclaresSolutionAndFrontendChecks()
    {
        var verification = File.ReadAllText(
            Path.Combine(GetRepositoryRoot(), "scripts", "verify.ps1"));

        Assert.Contains("\"test\", \"ICP-Platform.sln\"", verification, StringComparison.Ordinal);
        Assert.Contains("\"pnpm\", \"--dir\", \"frontend\", \"test\"", verification, StringComparison.Ordinal);

        foreach (var fullModeCheck in new[] { "diff-check", "dotnet-restore", "frontend-lint", "frontend-build" })
        {
            Assert.Contains(fullModeCheck, verification, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FrontendRootTestScript_RunsEveryWorkspaceThatDeclaresTests()
    {
        var repositoryRoot = GetRepositoryRoot();
        using var packageJson = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "frontend", "package.json")));

        var scripts = packageJson.RootElement.GetProperty("scripts");
        var rootTest = scripts.GetProperty("test").GetString();

        Assert.StartsWith(
            "pnpm run test:effect-network-lifecycle && pnpm run check:effect-network-lifecycle && ",
            rootTest,
            StringComparison.Ordinal);
        Assert.EndsWith("pnpm -r --if-present test", rootTest, StringComparison.Ordinal);
        Assert.Equal(
            "node --test scripts/check-effect-network-lifecycle.test.mjs",
            scripts.GetProperty("test:effect-network-lifecycle").GetString());
        Assert.Equal(
            "node scripts/check-effect-network-lifecycle.mjs",
            scripts.GetProperty("check:effect-network-lifecycle").GetString());
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
