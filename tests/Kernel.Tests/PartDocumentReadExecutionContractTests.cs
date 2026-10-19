using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Part;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PartDocumentReadExecutionContractTests
{
    [Theory]
    [InlineData("Part", "part.part", "api/part/parts")]
    [InlineData("Document", "document.document", "api/document/documents")]
    public void OwnedResource_ReadsUseFormalContractsAtTheirExistingPositions(string module, string resource, string route)
    {
        var descriptor = module == "Part" ? PartCrudDescriptor.Create() : DocumentCrudDescriptor.Create();
        Assert.True(descriptor.MapStandardEndpoints);
        Assert.Equal(route, descriptor.Route);
        Assert.Equal(module == "Part" ? Array.Empty<string>() : new[] { "create", "update", "delete" }, descriptor.LegacyDataExecutions);
        var compiled = new CanonicalModulePlanCompiler().Compile("owned-resource-reads",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray());
        Assert.True(compiled.IsSuccess, System.Text.Json.JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var entry = Assert.Single(plan.FeatureCatalog, point => point.ModuleId == module && point.BehaviorKey == resource + ".read");
        Assert.Equal("Workspace/navigation:workspace/module:" + module + "/navigation:" + (module == "Part" ? "part.parts" : "document.documents"), entry.Position);
        foreach (var operation in new[] { "list", "read" })
        {
            Assert.True(descriptor.DataExecutions.TryGetValue(operation, out var execution));
            Assert.Equal(resource + "." + operation, execution);
            var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == execution);
            Assert.Equal(resource + (operation == "list" ? ".list" : ".detail"), declaration.ViewKey);
            Assert.Equal("read", declaration.Surface);
            Assert.Equal(new[] { resource }, declaration.Resources);
            var positions = operation == "list" ? [entry.Position] : plan.FeatureCatalog
                .Where(point => point.ModuleId == module && (point.Position == entry.Position || point.Key == "edit"
                    && point.ReadBehaviors.Contains(resource + ".read", StringComparer.Ordinal)))
                .Select(point => point.Position).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(positions, declaration.FeaturePositions);
            Assert.Equal("standardCrud." + operation, declaration.Provider);
            Assert.Equal(new[] { "read" }, declaration.Actions);
            var requirement = Assert.Single(declaration.Requirements!);
            Assert.Equal(operation == "list" ? "query" : "target", requirement.Role);
            Assert.Equal(operation == "list" ? "query" : "object", requirement.Shape);
            Assert.Equal("read", requirement.Stage);
            Assert.Equal(new[] { "read" }, Assert.Single(requirement.AnyOf));
            Assert.Equal(operation == "list" ? 0 : 1, requirement.MinimumCount);
            Assert.Equal(operation == "list" ? null : (int?)1, requirement.MaximumCount);
        }
    }
}
