using System.Text.Json;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public class RepositoryTestEntrypointTests
{
    private static readonly string[] RequiredBackendTestProjects =
    [
        "tests/acceptance/Acceptance.Tests/Acceptance.Tests.csproj",
        "tests/Audit.Tests/Audit.Tests.csproj",
        "tests/Authorization.Tests/Authorization.Tests.csproj",
        "tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj",
        "tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj",
        "tests/Files.Tests/Files.Tests.csproj",
        "tests/HostApi.Tests/HostApi.Tests.csproj",
        "tests/Kernel.Tests/Kernel.Tests.csproj",
        "tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj",
        "tests/Modeling.Tests/Modeling.Tests.csproj",
        "tests/ModuleTemplateSample.Tests/ModuleTemplateSample.Tests.csproj",
        "tests/Organization.Tests/Organization.Tests.csproj",
        "tests/Persistence.Tests/Persistence.Tests.csproj",
        "tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj"
    ];

    [Fact]
    public void Solution_CoversEveryRequiredBackendTestProject()
    {
        var repositoryRoot = GetRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(repositoryRoot, "ICP-Platform.sln"))
            .Replace('\\', '/');

        foreach (var project in RequiredBackendTestProjects)
        {
            Assert.True(
                File.Exists(Path.Combine(repositoryRoot, project.Replace('/', Path.DirectorySeparatorChar))),
                $"缺少预期后端测试项目：{project}");
            Assert.Contains($"\"{project}\"", solution, StringComparison.OrdinalIgnoreCase);
        }
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
