using ICP.Platform.Kernel.ModulePlanning;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class AcceptanceFeatureCatalogTests
{
    [Theory]
    [InlineData("Organization")]
    [InlineData("Authorization")]
    [InlineData("Identity")]
    [InlineData("Audit")]
    [InlineData("Example")]
    public void RealAuthoring_ResolvesEveryGateRequestPosition(string module)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var documents = Directory.EnumerateFiles(Path.Combine(root.FullName, "modules", module, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonDocument.Parse(File.ReadAllText(path))).ToArray();
        try
        {
            using var capsule = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "modules", module, "newp.plugin.json")));
            var custom = FeatureCatalogCompiler.Read(capsule.RootElement, module);
            var points = FeatureCatalogCompiler.Compile([.. custom,
                .. StandardFeatureMetadataCompiler.Read(module, documents.Select(document => document.RootElement), custom)]);
            var catalog = new AcceptanceFeatureCatalog(points);
            if (module == "Audit")
            {
                Assert.Equal("audit.changes", catalog.Behavior(module, "audit.change.read").Key);
                return;
            }
            if (module == "Example")
            {
                var entry = catalog.Behavior(module, "example.record.read");
                Assert.Equal("example.records", entry.Key);
                Assert.Equal("example.record.create", catalog.Action(entry, "create", "list.pageActions").BehaviorKey);
                Assert.Equal("example.record.export", catalog.Action(entry, "export", "list").BehaviorKey);
                Assert.Equal("example", catalog.Behavior(module, "example.runtime-workload.read").Key);
                return;
            }
            if (module == "Identity")
            {
                var entry = catalog.Behavior(module, "identity.account.read");
                Assert.Equal("identity.accounts", entry.Key);
                Assert.Equal("identity.account.create", catalog.Action(entry, "create", "list.pageActions").BehaviorKey);
                foreach (var action in new[] { "edit", "enable", "disable", "delete", "credentialReset" })
                {
                    var row = catalog.Action(entry, action, "list.rowActions");
                    var detail = catalog.Action(entry, action, "detail.pageActions");
                    Assert.NotEqual(row.Position, detail.Position);
                    Assert.Equal(row.BehaviorKey, detail.BehaviorKey);
                    Assert.Contains(entry.Position, row.EntryDependencies);
                }
                return;
            }
            if (module == "Authorization")
            {
                var roleEntry = catalog.Behavior(module, "authorization.role.read");
                Assert.Equal("authorization.roles", roleEntry.Key);
                var create = catalog.Action(roleEntry, "create", "list.pageActions");
                Assert.Equal("authorization.role.create", create.BehaviorKey);
                Assert.Contains(roleEntry.Position, create.EntryDependencies);
                return;
            }
            foreach (var key in new[] { "structure", "companies", "departments", "positions", "persons" })
                Assert.Contains(catalog.OrganizationTab(key), points);
            foreach (var key in new[] { "companies", "departments", "persons" })
                Assert.Contains(catalog.Action(catalog.OrganizationTab(key), "create", "list.pageActions"), points);
            var persons = catalog.OrganizationTab("persons");
            foreach (var key in new[] { "edit", "delete", "disable", "enable" })
                Assert.Contains(catalog.Action(persons, key, "list.rowActions"), points);
            Assert.Contains(catalog.Action(persons, "linkUser", "detail.custom"), points);
        }
        finally { foreach (var document in documents) document.Dispose(); }
    }

    [Fact]
    public void ActionSelection_PreservesEntryAndSurfaceIdentityAndDeclaredDependencies()
    {
        var management = Point("management", "navigation", "organization.structure", null);
        var persons = Point("persons", "tab", "persons", "management");
        var structure = Point("tree", "tab", "structure", "management");
        var edit = Point("list-edit", "action", "edit", "persons") with
        { ActionScope = "list.rowActions", EntryDependencies = ["management", "persons"] };
        var treeEdit = Point("tree-edit", "action", "edit", "tree") with
        { ActionScope = "contextMenu", StaticScope = "person" };
        var catalog = new AcceptanceFeatureCatalog([management, persons, structure, edit, treeEdit]);

        Assert.Equal(edit, catalog.Action(catalog.OrganizationTab("persons"), "edit", "list.rowActions"));
        Assert.Equal(new[] { "management", "persons", "list-edit" }, catalog.GrantSet(edit));
        Assert.Throws<InvalidOperationException>(() => catalog.Action(persons, "edit", "contextMenu"));
    }

    [Fact]
    public void AmbiguousActionsFailInsteadOfChoosingTheFirstPosition()
    {
        var entry = Point("persons", "tab", "persons", "management");
        var action = Point("one", "action", "edit", "persons") with { ActionScope = "list.rowActions" };
        var catalog = new AcceptanceFeatureCatalog([entry, action, action with { Position = "two" }]);
        Assert.Throws<InvalidOperationException>(() => catalog.Action(entry, "edit", "list.rowActions"));
    }

    private static FeaturePoint Point(string position, string kind, string key, string? parent) =>
        new(position, "Organization", key, kind, key, parent, "", [], 0);
}
