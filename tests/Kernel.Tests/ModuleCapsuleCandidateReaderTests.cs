using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class ModuleCapsuleCandidateReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "icp-platform-tests",
        $"module-capsule-reader-{Guid.NewGuid():N}");

    [Fact]
    public void ReadBuiltIn_OrganizationCapsuleAndMetadata_ProducesCompilableCandidate()
    {
        var moduleRoot = Path.Combine(_root, "Organization");
        var navigationRoot = Path.Combine(moduleRoot, "metadata", "navigation");
        var modelsRoot = Path.Combine(moduleRoot, "metadata", "models");
        var viewsRoot = Path.Combine(moduleRoot, "metadata", "views");
        Directory.CreateDirectory(navigationRoot);
        Directory.CreateDirectory(modelsRoot);
        Directory.CreateDirectory(viewsRoot);
        File.WriteAllText(
            Path.Combine(moduleRoot, "plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "moduleId": "Organization",
              "displayName": "组织结构",
              "version": "1.0.0",
              "description": "组织结构管理模块",
              "entryAssembly": "../../OrganizationModule.dll",
              "dependencies": [],
              "backend": {
                "behaviorKey": "organization.backend"
              },
              "frontend": {
                "behaviorKey": "organization.frontend",
                "entry": "organization",
                "bindingMode": "behavior-bindings",
                "bindings": [
                  "organization.person.list",
                  "organization.person.detail",
                  "organization.tree.page"
                ]
              },
              "security": {
                "permissions": [
                  {
                    "code": "Organization.Person.View",
                    "displayName": "查看人员"
                  },
                  {
                    "code": "Organization.Person.Update",
                    "displayName": "更新人员"
                  }
                ]
              },
              "persistence": {
                "models": ["metadata/models/Person.model.json"],
                "migrations": {
                  "sqlServer": {
                    "assembly": "backend/Organization.SqlServerMigrations.dll",
                    "manifest": "backend/Organization.SqlServerMigrations.json"
                  }
                }
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(viewsRoot, "PersonDetail.view.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "view",
              "view": {
                "key": "organization.person.detail",
                "mode": "detail",
                "model": "Person",
                "sections": [
                  {
                    "key": "basic",
                    "titleKey": "Section_Basic",
                    "fields": ["ENAME"]
                  }
                ],
                "pageActions": [
                  {
                    "key": "edit",
                    "permissions": ["Organization.Person.Update"]
                  }
                ]
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(navigationRoot, "Organization.nav.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "nav",
              "navigation": {
                "items": [
                  {
                    "key": "organization.personCenter",
                    "path": "/organization/persons",
                    "entryType": "standard-list",
                    "targetKey": "organization.person.list",
                    "permissions": ["Organization.Person.View"]
                  },
                  {
                    "key": "organization.tree",
                    "path": "/organization/tree",
                    "entryType": "custom",
                    "targetKey": "organization.tree.page",
                    "permissions": ["Organization.Person.View"]
                  }
                ]
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(modelsRoot, "Person.model.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "Person": { "kind": "Entity" }
            }
            """);

        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(_root);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Organization", candidate.ModuleId);
        Assert.Equal("organization.backend", candidate.BackendBehaviorKey);
        Assert.Equal("organization.frontend", candidate.FrontendBehaviorKey);
        Assert.Equal("behavior-bindings", candidate.FrontendBindingMode);
        Assert.Equal(
            ["/organization/persons", "/organization/tree"],
            candidate.Routes.OrderBy(static route => route, StringComparer.Ordinal));
        Assert.Equal(
            ["organization.person.detail", "organization.person.list", "organization.tree.page"],
            candidate.FrontendBindingReferences.OrderBy(static key => key, StringComparer.Ordinal));
        Assert.Equal(["Person"], candidate.ModelKeys);
        Assert.NotNull(candidate.Persistence);
        Assert.Equal(["metadata/models/Person.model.json"], candidate.Persistence.ModelPaths);
        var bundle = Assert.Single(candidate.Persistence.MigrationBundles);
        Assert.Equal("SqlServer", bundle.Provider);
        Assert.Equal("backend/Organization.SqlServerMigrations.dll", bundle.AssemblyPath);
        Assert.Equal("backend/Organization.SqlServerMigrations.json", bundle.ManifestPath);

        var compilation = new CanonicalModulePlanCompiler().Compile("organization-adopter", candidates);
        Assert.True(compilation.IsSuccess, string.Join(Environment.NewLine, compilation.Diagnostics));
        var compiledPersistence = Assert.Single(compilation.Plan!.Modules).Persistence;
        Assert.NotNull(compiledPersistence);
        Assert.Equal(candidate.Persistence.ModelPaths, compiledPersistence.ModelPaths);
        Assert.Equal(bundle, Assert.Single(compiledPersistence.MigrationBundles));
    }

    [Fact]
    public void ReadBuiltIn_ExampleManifestDependencyFlowsIntoCanonicalPlanOrdering()
    {
        var runtimeModulesRoot = Path.Combine(_root, "runtime-modules");
        CopyBuiltInModuleToRuntimeView("Identity", runtimeModulesRoot);
        CopyBuiltInModuleToRuntimeView("Example", runtimeModulesRoot);

        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(runtimeModulesRoot);
        var example = Assert.Single(candidates, static candidate => candidate.ModuleId == "Example");

        Assert.Equal(["Identity"], example.Dependencies);

        var compilation = new CanonicalModulePlanCompiler().Compile("example-dependency", candidates);

        Assert.True(compilation.IsSuccess, string.Join(Environment.NewLine, compilation.Diagnostics.Select(static item => item.Message)));
        var compiledExample = Assert.Single(compilation.Plan!.Modules, static module => module.ModuleId == "Example");
        Assert.Equal(["Identity"], compiledExample.Dependencies);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReadBuiltIn_RealOrganizationDrawerViews_DoNotRequireFullPageBindings(bool trustedSnapshot, bool utf8Bom)
    {
        var runtimeModulesRoot = Path.Combine(_root, "runtime-modules");
        foreach (var moduleRoot in ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.EnumerateModuleRoots(FindRepositoryRoot()))
        {
            if (!File.Exists(Path.Combine(moduleRoot, "newp.plugin.json")))
            {
                continue;
            }
            CopyBuiltInModuleToRuntimeView(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ReadModuleId(moduleRoot), runtimeModulesRoot);
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(moduleRoot, "newp.plugin.json")));
            if (manifest.RootElement.TryGetProperty("entryAssembly", out var entry))
            {
                var assemblyName = Path.GetFileName(entry.GetString())!;
                File.Copy(Path.Combine(AppContext.BaseDirectory, assemblyName), Path.Combine(_root, assemblyName));
            }
        }

        if (utf8Bom)
        {
            // 编码是 authoring 输入的一部分；覆盖正式可信快照及普通候选读取，不改仓库源文件。
            foreach (var path in Directory.GetFiles(runtimeModulesRoot, "*.json", SearchOption.AllDirectories)
                         .Where(path => path.Contains(Path.DirectorySeparatorChar + "metadata" + Path.DirectorySeparatorChar)))
                File.WriteAllText(path, File.ReadAllText(path), new System.Text.UTF8Encoding(true));
        }
        var reader = new ModuleCapsuleCandidateReader();
        var candidates = trustedSnapshot
            ? reader.ReadTrustedBuiltIn(TrustedBuiltInModuleSource.CreateForHost(_root, runtimeModulesRoot))
            : reader.ReadBuiltIn(runtimeModulesRoot);
        var organization = Assert.Single(candidates, candidate => candidate.ModuleId == "Organization");
        Assert.DoesNotContain("organization.company.detail", organization.FrontendBindingReferences);
        Assert.DoesNotContain("organization.department.detail", organization.FrontendBindingReferences);
        Assert.DoesNotContain("organization.position.detail", organization.FrontendBindingReferences);
        Assert.Contains("organization.person.detail", organization.FrontendBindingReferences);
        var result = new CanonicalModulePlanCompiler().Compile("required-object-detail", candidates);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void CopyBuiltInModuleToRuntimeView(string moduleId, string runtimeModulesRoot)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceModuleRoot = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repositoryRoot, moduleId);
        var runtimeModuleRoot = Path.Combine(runtimeModulesRoot, moduleId);
        Directory.CreateDirectory(runtimeModuleRoot);
        File.Copy(
            Path.Combine(sourceModuleRoot, "newp.plugin.json"),
            Path.Combine(runtimeModuleRoot, "plugin.json"),
            overwrite: true);
        CopyDirectory(
            Path.Combine(sourceModuleRoot, "metadata"),
            Path.Combine(runtimeModuleRoot, "metadata"));
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        if (!Directory.Exists(sourceRoot)) return;
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceRoot, targetRoot, StringComparison.Ordinal));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var targetPath = file.Replace(sourceRoot, targetRoot, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
