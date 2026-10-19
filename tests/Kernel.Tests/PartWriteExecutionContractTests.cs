using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Part;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PartWriteExecutionContractTests
{
    [Fact]
    public void PartCreate_ProjectsItsExistingEntryAndInternalWritesHaveNoInventedEntry()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("part-write-consumer",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var descriptor = PartCrudDescriptor.Create();
        Assert.Empty(descriptor.LegacyDataExecutions);
        Assert.Equal(4, descriptor.LegacyObjectOperationBindings.Count);
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "part.part.create");
        Assert.Equal(("part.part.list", "pageActions", "create", "standardCrud.create"),
            (declaration.ViewKey, declaration.Surface, declaration.ActionKey, declaration.Provider));
        var feature = Assert.Single(plan.FeatureCatalog, point => point.ModuleId == "Part" && point.BehaviorKey == "part.part.create");
        Assert.Equal(new[] { feature.Position }, declaration.FeaturePositions);
        Assert.StartsWith("Workspace/navigation:workspace/module:Part/navigation:part.parts/", feature.Position);
        var candidate = Assert.Single(declaration.Requirements!);
        Assert.Equal(("candidate", "submit", "candidate"), (candidate.Role, candidate.Stage, candidate.Shape));
        Assert.Equal(new[] { "create" }, Assert.Single(candidate.AnyOf));
        var owner = Assert.Single(plan.Modules, module => module.ModuleId == "Part");
        foreach (var operation in new[] { "create", "update", "delete" })
        {
            var key = operation == "create" ? "part.part.internal.create" : "part.part." + operation;
            var contract = Assert.Single(owner.DataExecutions, item => item.Key == key);
            Assert.Equal("internal", contract.Boundary);
            Assert.Equal("standardCrud." + operation, contract.Provider);
            Assert.Empty(contract.FeatureBehaviors);
            Assert.NotEmpty(contract.FixedBindings);
            Assert.DoesNotContain(plan.ActionDataAccessCatalog, item => item.Execution == key);
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICP-Platform.sln"))) root = root.Parent;
        var path = Path.Combine(root!.FullName, "tests/Kernel.Tests/Fixtures/PartWriteDataAccess.contract.json");
        var json = JsonSerializer.Serialize(new[] { declaration }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_PART_WRITE_FIXTURE_EXPORT") == "1")
            File.WriteAllText(path, json.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        else Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path))),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)));
    }
    [Theory]
    [InlineData("list")]
    [InlineData("read")]
    [InlineData("options")]
    [InlineData("export")]
    public void InternalWriteAdapter_DoesNotBroadenStandardReadContractParsing(string operation)
    {
        using var capsule = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            dataExecutions = new[] { new { key = "part.invalid", moduleId = "Part", provider = "standardCrud." + operation,
                boundary = "internal", featureBehaviors = Array.Empty<string>(), resourceKey = "part.part",
                fixedBindings = new[] { new { kind = "internal", identity = "part.invalid", caller = "Caller.Execute" } } } }
        }));
        var error = Assert.Throws<InvalidOperationException>(() => DataExecutionContracts.Read(capsule.RootElement, "Part"));
        Assert.Equal("DATA_EXECUTION_REQUIREMENT_INVALID", error.Message);
    }
}
