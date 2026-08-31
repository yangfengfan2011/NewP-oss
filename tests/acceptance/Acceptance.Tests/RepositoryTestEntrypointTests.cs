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

    // verify.ps1 是仓库唯一的验证入口，没有 CI 兜底。它至少要覆盖整个 solution
    // 和前端测试；-Quick 只允许省掉冻结候选专用的步骤，不能省掉门禁本身。
    [Fact]
    public void VerificationEntrypoint_CoversTheSolutionAndFrontendInBothModes()
    {
        var verification = File.ReadAllText(
            Path.Combine(GetRepositoryRoot(), "scripts", "verify.ps1"));

        Assert.Contains("\"test\", \"ICP-Platform.sln\"", verification, StringComparison.Ordinal);
        Assert.Contains("\"pnpm\", \"--dir\", \"frontend\", \"test\"", verification, StringComparison.Ordinal);

        // -Quick 只包裹这三步；其余阶段必须无条件执行。
        foreach (var quickOnlySkip in new[] { "diff-check", "dotnet-restore", "frontend-build" })
        {
            Assert.Contains(quickOnlySkip, verification, StringComparison.Ordinal);
        }
        Assert.Equal(2, CountOccurrences(verification, "if (-not $Quick)"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
