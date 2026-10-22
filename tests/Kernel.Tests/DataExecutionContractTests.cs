using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class DataExecutionContractTests
{
    [Fact]
    public void ExampleContract_ProvidesCandidateAndPreEditRequirementsWithoutRepeatingTheFloor()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var compiled = new CanonicalModulePlanCompiler().Compile("example-contract", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var declarations = compiled.Plan!.ActionDataAccessCatalog.Where(item => item.ModuleId == "Example").ToArray();
        Assert.NotEmpty(declarations);
        // 旧执行条目不重复provider下限；导出按钮的read由唯一按钮声明拥有。
        Assert.All(candidates.Single(candidate => candidate.ModuleId == "Example").Features.SelectMany(feature => feature.DataAccess)
            .Where(declaration => declaration.BusinessBinding is null), declaration => Assert.Empty(declaration.Actions));
        var create = Assert.Single(declarations, item => item.BusinessBinding == "example.record.create");
        Assert.Equal("ExampleRecord", Assert.Single(create.GetObjectRequirements()).Object);
        Assert.Equal(new[] { "create" }, Assert.Single(create.GetObjectRequirements()).Actions);
        var updates = declarations.Where(item => item.BusinessBinding == "example.record.update").ToArray();
        Assert.NotEmpty(updates);
        Assert.All(updates, item => Assert.Equal(new[] { "edit" }, Assert.Single(item.GetObjectRequirements()).Actions));
        Assert.DoesNotContain(declarations, item => item.Execution is "example.record.create" or "example.record.update" or "example.record.delete");
        Assert.All(declarations.Where(item => item.Execution == "example.record.read"), item =>
        {
            var requirement = Assert.Single(item.Requirements!);
            Assert.Equal("read", requirement.Stage);
            Assert.Equal(new[] { "read" }, Assert.Single(requirement.AnyOf));
        });
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var json = JsonSerializer.Serialize(declarations, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_EXECUTION_CONTRACT_FIXTURE") is { } file)
        {
            File.WriteAllText(Path.Combine(root!.FullName, ".codex-temp", "issue-305", file), json);
            var source = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Example"), "metadata", "models", "ExampleRecord.model.json");
            File.WriteAllBytes(Path.Combine(root.FullName, ".codex-temp", "issue-305", "ExampleRuntimeModel.contract.json"),
                new ICP.Platform.Modeling.Projection.ModelRuntimeMetadataProjector().Project(File.ReadAllBytes(source), source));
        }
        var fixture = Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "ExampleCrudDataAccess.contract.json");
        if (File.Exists(fixture)) Assert.Equal(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(fixture)).GetRawText().Replace("\r", ""),
            JsonSerializer.Deserialize<JsonElement>(json).GetRawText().Replace("\r", ""));
        var modelSource = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root.FullName, "Example"), "metadata", "models", "ExampleRecord.model.json");
        var projectedModel = new ICP.Platform.Modeling.Projection.ModelRuntimeMetadataProjector().Project(File.ReadAllBytes(modelSource), modelSource);
        Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(projectedModel)),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root.FullName, "tests", "Kernel.Tests", "Fixtures", "ExampleRuntimeModel.contract.json")))));
    }

    [Fact]
    public void ExecutionContracts_RejectTheRetiredLegacyActionMode()
    {
        using var json = JsonDocument.Parse("""
            {"dataExecutions":[{"key":"test.execution","moduleId":"Test","provider":"test.backend","boundary":"internal",
              "featureBehaviors":[],"requirements":[{"role":"target","stage":"read","shape":"object","resources":["test.item"],"anyOf":[["read"]]}],
              "fixedBindings":[{"kind":"internal","identity":"test.execution","caller":"Test.Consumer.ReadAsync"}],"legacyActions":true}]}
            """);
        Assert.Throws<JsonException>(() => DataExecutionContracts.Read(json.RootElement, "Test"));
    }
    [Theory]
    [InlineData("provider")]
    [InlineData("owner")]
    [InlineData("lower-floor")]
    [InlineData("shape")]
    [InlineData("actions")]
    [InlineData("unbound")]
    public void FormalExample_RejectsInvalidExecutionAndLowerFloorRewrites(string problem)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Example");
        var contracts = candidates[owner].DataExecutions.ToArray();
        var index = Array.FindIndex(contracts, contract => contract.Provider == "standardCrud.read");
        var contract = contracts[index];
        contracts[index] = problem switch
        {
            "provider" => contract with { Provider = "unregistered.provider" },
            "owner" => contract with { ModuleId = "Workspace" },
            "lower-floor" => contract with { Requirements = [contract.Requirements[0] with { AnyOf = [["edit"]] }] },
            "shape" => contract with { Requirements = [contract.Requirements[0] with { Shape = "candidate" }] },
            _ => contract
        };
        var features = candidates[owner].Features.Select(feature => feature with { DataAccess = feature.DataAccess.Select(declaration =>
            problem == "actions" && declaration.Execution == contract.Key ? declaration with { Actions = ["read"] } : declaration)
            .Where(declaration => problem != "unbound" || declaration.Execution != contract.Key).ToArray() }).ToArray();
        candidates[owner] = candidates[owner] with { DataExecutions = contracts, Features = features };
        Assert.False(new CanonicalModulePlanCompiler().Compile(problem, candidates).IsSuccess);
    }

    [Fact]
    public void FormalPlan_UnknownExecutionCannotEnableADataEntry()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var compiled = new CanonicalModulePlanCompiler().Compile("before-execution-contract", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (Environment.GetEnvironmentVariable("NEWP_EXECUTION_SNAPSHOT") is { } file)
            File.WriteAllText(Path.Combine(root!.FullName, ".codex-temp", "issue-305", file),
                JsonSerializer.Serialize(new { candidate = Environment.GetEnvironmentVariable("NEWP_EXECUTION_SNAPSHOT_CANDIDATE") ?? "7457263f + measurement fixture", compiled.Plan!.FeatureCatalog }, new JsonSerializerOptions { WriteIndented = true }));
        var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Workspace");
        var features = candidates[owner].Features.ToArray();
        var index = Array.FindIndex(features, feature => feature.DataAccess.Count > 0);
        features[index] = features[index] with { DataAccess = [features[index].DataAccess[0] with { Execution = "unregistered.execution" }] };
        candidates[owner] = candidates[owner] with { Features = features };
        Assert.False(new CanonicalModulePlanCompiler().Compile("unknown-execution", candidates).IsSuccess);
    }
}
