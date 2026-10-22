using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class RetiredFrontendImplementationTests
{
    [Fact]
    public void Repository_ContainsNoRetiredFrontendImplementationSources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var frontendModulesRoot = Path.Combine(repositoryRoot, "frontend", "modules");
        var retiredModuleDirectories = new[]
        {
            Path.Combine("platform", "config-center"),
            Path.Combine("example", "demo"),
            Path.Combine("platform", "files"),
            Path.Combine("platform", "metadata-config-center"),
            Path.Combine("example", "module-template-sample"),
            Path.Combine("platform", "plugin-ops")
        };
        var remainingSources = new List<string>();

        foreach (var moduleDirectory in retiredModuleDirectories)
        {
            var moduleRoot = Path.Combine(frontendModulesRoot, moduleDirectory);
            foreach (var rootFile in new[] { "package.json", "tsconfig.json", "README.md" })
            {
                var path = Path.Combine(moduleRoot, rootFile);
                if (File.Exists(path))
                {
                    remainingSources.Add(Path.GetRelativePath(repositoryRoot, path));
                }
            }

            var sourceRoot = Path.Combine(moduleRoot, "src");
            if (Directory.Exists(sourceRoot))
            {
                remainingSources.AddRange(Directory
                    .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(repositoryRoot, path)));
            }
        }

        foreach (var relativePath in new[]
                 {
                     // 正式 Audit/Authorization 已重新接入 Standard CRUD，只禁止历史自定义页面。
                     Path.Combine("frontend", "modules", "platform", "audit", "src", "pages", "AccessLogs.tsx"),
                     Path.Combine("frontend", "modules", "platform", "audit", "src", "pages", "OperationLogs.tsx"),
                     Path.Combine("frontend", "modules", "platform", "authorization", "src", "pages", "PermissionList.tsx"),
                     Path.Combine("frontend", "modules", "platform", "authorization", "src", "pages", "RoleManagement.tsx"),
                     Path.Combine("frontend", "modules", "platform", "organization", "src", "pages", "OrganizationTreePage.tsx"),
                     Path.Combine("frontend", "modules", "platform", "organization", "src", "pages", "PositionManagementPage.tsx"),
                     Path.Combine("frontend", "modules", "platform", "organization", "src", "components", "OrganizationTreePanel.tsx")
                 })
        {
            var path = Path.Combine(repositoryRoot, relativePath);
            if (File.Exists(path))
            {
                remainingSources.Add(relativePath);
            }
        }

        Assert.True(
            remainingSources.Count == 0,
            $"Retired frontend implementation sources remain:{Environment.NewLine}"
            + string.Join(Environment.NewLine, remainingSources.Order(StringComparer.Ordinal)));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "apps", "host-api", "Program.cs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
