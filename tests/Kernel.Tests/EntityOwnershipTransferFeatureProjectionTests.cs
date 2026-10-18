using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.Kernel.Tests;

public sealed class EntityOwnershipTransferFeatureProjectionTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void NotificationEntryHasOnlyItsOwnFeature_AndObjectAliasUsesExistingWorkspaceRead()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Workspace");
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var points = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Workspace", documents,
            FeatureCatalogCompiler.Read(capsule.RootElement, "Workspace"), true));
        var notifications = Assert.Single(points, point => point.Key == "workspace.transfers" && point.Kind == "navigation");
        var read = Assert.Single(points, point => point.Key == "workspace" && point.Kind == "navigation");
        Assert.Equal("workspace.transfers.read", notifications.BehaviorKey);
        Assert.Equal(read.Position, Assert.Single(notifications.EntryDependencies));
        Assert.DoesNotContain(points, point => point.Key == "workspace.transfers.object" || (point.ActionScope is not null && point.Position.Contains("workspace.transfers", StringComparison.Ordinal)));
        var nav = documents.Single(document => document.TryGetProperty("navigation", out _)).GetProperty("navigation").GetProperty("items");
        var alias = nav.EnumerateArray().Single(item => item.GetProperty("key").GetString() == "workspace.transfers.object");
        Assert.Equal("workspace", alias.GetProperty("featureKey").GetString());
        FeatureCatalogCompiler.ValidateSelection(points, [read.Position, notifications.Position]);
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.FullName, "tests", "Kernel.Tests", "Fixtures", "WorkspaceTransferNotificationFeatures.contract.json")));
        Assert.Equal(fixture.RootElement.GetProperty("readPosition").GetString(), read.Position);
        Assert.Equal(fixture.RootElement.GetProperty("notificationsPosition").GetString(), notifications.Position);
        output.WriteLine(JsonSerializer.Serialize(new { readPosition = read.Position, notificationsPosition = notifications.Position }));
    }

    [Theory, Trait("Category", "ModuleMetadataConsumer")]
    [InlineData("Workspace", "workspace.folder", "workspace")]
    [InlineData("Part", "part.part", "part.parts")]
    [InlineData("Document", "document.document", "document.documents")]
    public void TransferUsesRealOwnerActionsAndReadAliases(string module, string resource, string entryKey)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, module);
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var points = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read(module, documents,
            FeatureCatalogCompiler.Read(capsule.RootElement, module), includeCustomFeatures: true));
        var read = Assert.Single(points, point => point.Kind == "navigation" && point.Key == entryKey);
        var row = Assert.Single(points, point => point.Key == "transfer" && point.ActionScope == "list.rowActions");
        Assert.Equal(resource + ".transfer", row.BehaviorKey);
        Assert.Contains(resource + ".read", row.ReadBehaviors);
        Assert.Equal(read.Position, Assert.Single(row.EntryDependencies));
        FeatureCatalogCompiler.ValidateSelection(points, [read.Position, row.Position]);
        var batch = points.SingleOrDefault(point => point.Key == "transferSelected" && point.ActionScope == "list.pageActions");
        if (module == "Workspace")
        {
            Assert.NotNull(batch);
            Assert.Equal(resource + ".transfer", batch.BehaviorKey);
            Assert.Contains(resource + ".read", batch.ReadBehaviors);
        }
        else Assert.Null(batch);
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.FullName, "tests", "Kernel.Tests", "Fixtures", module + "TransferFeatures.contract.json")));
        Assert.Equal(fixture.RootElement.GetProperty("readPosition").GetString(), read.Position);
        Assert.Equal(fixture.RootElement.GetProperty("transferPosition").GetString(), row.Position);
        Assert.Equal(fixture.RootElement.GetProperty("batchPosition").GetString(), batch?.Position);
        output.WriteLine(JsonSerializer.Serialize(new { module, readPosition = read.Position, transferPosition = row.Position, batchPosition = batch?.Position }));
    }
}
