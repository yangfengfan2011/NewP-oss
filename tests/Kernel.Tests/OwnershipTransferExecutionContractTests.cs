using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class OwnershipTransferExecutionContractTests
{
    [Fact]
    public void CentralTransferProviderFixesEditAtPrepareAndSubmitWithTheExistingReadDependency()
    {
        var contracts = Read();
        var contract = Assert.Single(DataExecutionContracts.Compile(contracts, Actions(readRequired: true)).Values);
        Assert.Equal("entityOwnership.transfer", contract.Provider);
        Assert.Null(contract.ProviderModuleId);
        Assert.Equal(new[] { "prepare", "submit" }, contract.Requirements.Select(requirement => requirement.Stage));
        Assert.All(contract.Requirements, requirement =>
        {
            Assert.Equal("target", requirement.Role); Assert.Equal("objects", requirement.Shape);
            Assert.Equal(new[] { "part.part" }, requirement.Resources);
            Assert.Equal(new[] { "edit" }, Assert.Single(requirement.AnyOf));
            Assert.Equal(1, requirement.MinimumCount); Assert.Null(requirement.MaximumCount);
        });
    }

    [Fact]
    public void TransferCannotSilentlyLoseTheGlobalEditReadClosure()
    {
        Assert.Throws<InvalidOperationException>(() => DataExecutionContracts.Compile(Read(), Actions(readRequired: false)));
    }

    [Fact]
    public void InitializationFixesPendingQueryAndTheOriginalReadThenEditRoles()
    {
        var contract = Assert.Single(DataExecutionContracts.Compile(Read(operation: "initialize"), Actions(readRequired: true)).Values);
        Assert.Equal("entityOwnership.initialize", contract.Provider);
        Assert.Equal(new[] { ("query", "read", "query"), ("visibility", "prepare", "objects"), ("target", "prepare", "objects"),
            ("visibility", "submit", "objects"), ("target", "submit", "objects") },
            contract.Requirements.Select(requirement => (requirement.Role, requirement.Stage, requirement.Shape)));
        Assert.All(contract.Requirements, requirement =>
        {
            Assert.Equal(new[] { "part.part" }, requirement.Resources);
            Assert.Equal(new[] { requirement.Role == "target" ? "edit" : "read" }, Assert.Single(requirement.AnyOf));
            Assert.Equal(requirement.Role == "query" ? 0 : 1, requirement.MinimumCount);
            Assert.Null(requirement.MaximumCount);
        });
    }

    [Theory]
    [InlineData(",\"resourceKey\":\"part.part\"")]
    [InlineData(",\"requirements\":[]")]
    [InlineData(",\"legacyActions\":true")]
    public void ConsumerCannotReplaceTheBuiltInFloorOrResourceArray(string extra)
    {
        Assert.Throws<InvalidOperationException>(() => Read(extra));
    }

    [Theory]
    [InlineData("transfer")]
    [InlineData("initialize")]
    public void CentralOwnershipFloor_RejectsTheOtherProviderModesProjectedResourceSet(string operation)
    {
        var contract = Assert.Single(Read(operation: operation));
        Assert.Throws<InvalidOperationException>(() => DataExecutionContracts.Compile(
            [contract with { TargetResources = ["part.part"] }], Actions(readRequired: true)));
    }

    [Fact]
    public void ActualOwnershipBindingsUseTheSameCanonicalOutputAsThePageConsumers()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var result = new CanonicalModulePlanCompiler().Compile("ownership-formal-bindings", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var declarations = result.Plan!.ActionDataAccessCatalog.Where(item => item.Provider is "entityOwnership.transfer" or "entityOwnership.initialize"
            || item.Execution == "workspace.folder.initializeOwnership").ToArray();
        Assert.NotEmpty(declarations);
        Assert.All(declarations.Where(item => item.Provider == "entityOwnership.transfer"), declaration =>
        {
            Assert.False(string.IsNullOrWhiteSpace(declaration.ActionKey));
            Assert.Contains(declaration.Surface, new[] { "rowActions", "pageActions" });
            Assert.All(declaration.Requirements!, requirement => Assert.Equal(new[] { "edit" }, Assert.Single(requirement.AnyOf)));
            Assert.All(declaration.FeaturePositions, position => Assert.Contains(result.Plan.FeatureCatalog,
                feature => feature.Position == position && feature.BehaviorKey == declaration.Execution));
        });
        var workspace = Assert.Single(result.Plan.Modules, module => module.ModuleId == "Workspace");
        var initialization = Assert.Single(workspace.DataExecutions, contract => contract.Key == "workspace.folder.initializeOwnership");
        Assert.Equal("workspace.backend", initialization.Provider);
        Assert.Equal(new[] { ("query", "read"), ("target", "prepare"), ("target", "submit") },
            initialization.Requirements.Select(requirement => (requirement.Role, requirement.Stage)));
        Assert.All(initialization.Requirements, requirement => Assert.Equal(new[] { requirement.Role == "query" ? "read" : "edit" }, Assert.Single(requirement.AnyOf)));
        Assert.DoesNotContain(initialization.Requirements, requirement => requirement.Role == "visibility");
        Assert.All(new[] { (Key: "workspace.ownership.display.targets", Caller: "ICP.Platform.Crud.Ownership.EntityOwnershipTransferDisplay.FindNamesAsync"),
            (Key: "workspace.ownership.display.senders", Caller: "ICP.Platform.Modules.Workspace.WorkspaceTransferNotifications.ListAsync") }, entry =>
        {
            var source = Assert.Single(workspace.DataExecutions, contract => contract.Key == entry.Key);
            Assert.Equal("internal", source.Boundary);
            Assert.Equal(entry.Caller, Assert.Single(source.FixedBindings).Caller);
            Assert.DoesNotContain(result.Plan.ActionDataAccessCatalog, item => item.Execution == entry.Key);
        });
        var json = JsonSerializer.Serialize(declarations, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_OWNERSHIP_DATA_ACCESS_EXPORT_PATH") is { Length: > 0 } export)
            File.WriteAllText(export, json);
        else
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            Assert.NotNull(root);
            Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)),
                JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root.FullName,
                    "tests", "Kernel.Tests", "Fixtures", "OwnershipDataAccess.contract.json")))));
        }
    }

    [Fact]
    public void OwnershipProviderPrefixDoesNotMakeAnUnknownCapabilityBuiltIn()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var index = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Part");
        candidates[index] = candidates[index] with { DataExecutions = candidates[index].DataExecutions.Select(contract =>
            contract.Provider == "entityOwnership.transfer" ? contract with { Provider = "entityOwnership.unregistered" } : contract).ToArray() };
        Assert.False(new CanonicalModulePlanCompiler().Compile("unregistered-ownership-provider", candidates).IsSuccess);
    }

    private static IReadOnlyList<DataExecutionContract> Read(string extra = "", string operation = "transfer")
    {
        var json = """
            {"dataExecutions":[{"key":"part.part.transfer","moduleId":"Part","provider":"entityOwnership.transfer",
            "boundary":"entry","featureBehaviors":["part.part.transfer"],"resources":["part.part"]
            """;
        using var document = JsonDocument.Parse(json.Replace(".transfer", "." + operation, StringComparison.Ordinal) + extra + "}]}");
        return DataExecutionContracts.Read(document.RootElement, "Part");
    }

    private static DataActionDeclaration[] Actions(bool readRequired) =>
        [new("Authorization", "read", "读取", "data.read"), new("Authorization", "edit", "编辑", "data.edit", RequiresRead: readRequired)];
}
