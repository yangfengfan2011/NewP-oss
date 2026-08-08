using ICP.Platform.Kernel.ModulePlanning;
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
        Assert.Equal(
            ["Organization.Person.Update", "Organization.Person.View"],
            candidate.PermissionCodes.OrderBy(static code => code, StringComparer.Ordinal));
        Assert.Equal(
            ["Organization.Person.Update", "Organization.Person.View"],
            candidate.PermissionReferences.Distinct(StringComparer.Ordinal).OrderBy(static code => code, StringComparer.Ordinal));
        Assert.Equal(["Organization.Person"], candidate.ModelKeys);

        var compilation = new CanonicalModulePlanCompiler().Compile("organization-adopter", candidates);
        Assert.True(compilation.IsSuccess, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
