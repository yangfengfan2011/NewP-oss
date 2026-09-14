using ICP.Platform.Kernel.ModulePlanning;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class AcceptanceFeatureCatalogTests
{
    [Fact]
    public void RealOrganizationAuthoring_ResolvesEveryGateRequestPosition()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var documents = Directory.EnumerateFiles(Path.Combine(root.FullName, "modules", "Organization", "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonDocument.Parse(File.ReadAllText(path))).ToArray();
        try
        {
            var points = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Organization", documents.Select(document => document.RootElement)));
            var catalog = new AcceptanceFeatureCatalog(points);
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
