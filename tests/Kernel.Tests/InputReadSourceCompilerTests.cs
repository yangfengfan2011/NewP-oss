using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class InputReadSourceCompilerTests
{
    [Fact]
    public void ActualPartPermissionPage_ProjectsOnlyItsReadSourceAtTheInputPosition()
    {
        var candidates = Candidates();
        var index = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Part");
        candidates[index] = WithInput(candidates[index], "authorize", "part.part.permissions", ["read", "authorize"]);
        var plan = Compile(candidates);
        var button = Assert.Single(plan, item => item.ViewKey == "part.part.list" && item.Surface == "rowActions" && item.ActionKey == "authorize");
        var read = Assert.Single(plan, item => item.ViewKey == "part.part.permissions" && item.Surface == "read");
        Assert.Contains(Assert.Single(button.FeaturePositions), read.FeaturePositions);
        Assert.Equal(new[] { "read" }, read.Actions);
        Assert.Equal("part.part.authorization.view", read.Execution);
        Assert.Equal("authorization.object.view", read.Provider);
        Assert.Equal("prepare", Assert.Single(read.Requirements!).Stage);
        Assert.Equal(new[] { "read", "authorize" }, button.Actions);
        var list = Assert.Single(plan, item => item.ViewKey == "part.part.list" && item.Surface == "read");
        Assert.DoesNotContain(Assert.Single(button.FeaturePositions), list.FeaturePositions);
    }

    [Theory]
    [InlineData("unknown.source")]
    [InlineData("part.part.authorization.manage")]
    [InlineData("document.document.permissions")]
    [InlineData("part.part.initializeOwnership")]
    public void InputReference_RejectsUnknownForeignOrMixedWriteSources(string source)
    {
        var candidates = Candidates();
        var index = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Part");
        candidates[index] = WithInput(candidates[index], "authorize", source, ["read", "authorize"]);
        Assert.Equal("INPUT_READ_SOURCE_INVALID", Assert.Throws<InvalidOperationException>(() => Compile(candidates)).Message);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("submit")]
    [InlineData("feature")]
    public void InputReference_RejectsConflictingContentSubmitOrWrongFeatureSources(string failure)
    {
        var candidates = Candidates();
        var index = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Part");
        candidates[index] = WithInput(candidates[index], "authorize", "part.part.permissions", ["read", "authorize"]);
        if (failure == "content")
        {
            var original = candidates[index].Features.SelectMany(feature => feature.DataAccess).First(item => item.ViewKey == "part.part.permissions");
            candidates[index] = candidates[index] with { Features = candidates[index].Features.Select(feature => feature with
            {
                DataAccess = feature.DataAccess.Any(item => item.InputReadSourceKey is not null)
                    ? [.. feature.DataAccess, original with { Execution = "part.part.list" }] : feature.DataAccess
            }).ToArray() };
        }
        if (failure == "submit")
        {
            var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Authorization");
            candidates[owner] = candidates[owner] with { DataExecutionProviders = candidates[owner].DataExecutionProviders.Select(provider =>
                provider.Key == "authorization.object.view" ? provider with { Requirements = provider.Requirements.Select(item => item with { Stage = "submit" }).ToArray() } : provider).ToArray() };
        }
        if (failure == "feature") candidates[index] = candidates[index] with { DataExecutions = candidates[index].DataExecutions.Select(contract =>
            contract.Key == "part.part.authorization.view" ? contract with { FeatureBehaviors = ["part.part.other"] } : contract).ToArray() };
        Assert.Equal("INPUT_READ_SOURCE_INVALID", Assert.Throws<InvalidOperationException>(() => Compile(candidates)).Message);
    }

    [Fact]
    public void ActualOwnershipInput_RetainsTheOriginalQueryWithoutTheOldWriteRoles()
    {
        var candidates = Candidates();
        var index = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Part");
        candidates[index] = WithInput(candidates[index], "initializeOwnership", "part.part.initializeOwnership", ["edit"]);
        using var capsule = JsonDocument.Parse("""
            {"dataExecutions":[{"key":"part.part.initializeOwnership","moduleId":"Part","provider":"entityOwnership.initialize",
              "boundary":"entry","featureBehaviors":["part.part.initializeOwnership"],"requirements":[
              {"role":"query","stage":"read","shape":"query","resources":["part.part"],"anyOf":[["read"]],"minimumCount":0,"maximumCount":null}]}]}
            """);
        var query = Assert.Single(DataExecutionContracts.Read(capsule.RootElement, "Part"));
        candidates[index] = candidates[index] with { DataExecutions = candidates[index].DataExecutions
            .Select(item => item.Key == query.Key ? query : item).ToArray() };
        var plan = Compile(candidates);
        var read = Assert.Single(plan, item => item.ViewKey == query.Key && item.Surface == "read");
        Assert.Equal(query.Key, read.Execution);
        Assert.Equal("entityOwnership.initialize", read.Provider);
        Assert.Equal(new[] { "read" }, read.Actions);
        Assert.Equal("query", Assert.Single(read.Requirements!).Role);
        Assert.Equal(0, Assert.Single(read.Requirements!).MinimumCount);
        Assert.Null(Assert.Single(read.Requirements!).MaximumCount);
        Assert.All(plan.Where(item => item.BusinessBinding == query.Key), button =>
        {
            Assert.Equal(new[] { "edit" }, button.Actions);
            Assert.All(button.FeaturePositions, position => Assert.Contains(position, read.FeaturePositions));
            Assert.Null(button.Requirements);
        });
        Assert.DoesNotContain(plan, item => item.Execution == query.Key && item.Surface != "read");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualWorkspaceInput_PreservesTheRegisteredHeterogeneousReadSource(bool query)
    {
        var candidates = Candidates();
        var index = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Workspace");
        var sourceKey = query ? "workspace.folder.initializeOwnership" : "workspace.content.permissions";
        var behavior = query ? "workspace.folder.initializeOwnership" : "workspace.folder.authorize";
        candidates[index] = WithInput(candidates[index], query ? "initializeOwnership" : "authorize", sourceKey, query ? ["edit"] : ["read", "authorize"]);
        if (query) candidates[index] = candidates[index] with { DataExecutions = candidates[index].DataExecutions.Select(contract => contract.Key == sourceKey
            ? contract with { Requirements = contract.Requirements.Where(item => item.Role == "query").ToArray() } : contract).ToArray() };
        var plan = Compile(candidates);
        var read = Assert.Single(plan, item => item.ViewKey == sourceKey && item.Surface == "read");
        Assert.Equal(new[] { "workspace.folder", "part.part", "document.document" }, read.Resources);
        Assert.Equal(new[] { "read" }, read.Actions);
        var buttons = plan.Where(item => item.BusinessBinding == behavior).ToArray();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button =>
        {
            Assert.Equal(3, button.GetObjectRequirements().Count);
            Assert.All(button.FeaturePositions, position => Assert.Contains(position, read.FeaturePositions));
        });
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("[]")]
    public void AuthoringInputReference_RejectsNonStringOrEmptyValues(string source)
    {
        using var navigation = JsonDocument.Parse("""{"navigation":{"items":[{"key":"items","featureAuthorization":true,"targetKey":"items"}]}}""");
        using var model = JsonDocument.Parse("""{"Part":{"kind":"Entity"}}""");
        using var view = JsonDocument.Parse("""
            {"view":{"key":"items","mode":"list","model":"Part","resourceKey":"part.part","pageActions":[
              {"key":"initialize","handlerKey":"initializeOwnership","inputReadSourceKey":SOURCE,"dataAccess":{"object":"Part","actions":["edit"]}}]}}
            """.Replace("SOURCE", source, StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => StandardFeatureMetadataCompiler.Read("Part", [navigation.RootElement, model.RootElement, view.RootElement]));
    }

    private static ModuleCapsuleCandidate[] Candidates() => new ModuleCapsuleCandidateReader()
        .ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();

    private static IReadOnlyList<ActionDataAccessRequirement> Compile(ModuleCapsuleCandidate[] candidates) => ActionDataAccessCompiler.Compile(
        candidates.SelectMany(candidate => candidate.Features).ToArray(), candidates.SelectMany(candidate => candidate.DataActions).ToArray(),
        candidates.SelectMany(candidate => candidate.DataExecutions).ToArray(), candidates.SelectMany(candidate => candidate.DataExecutionProviders).ToArray());

    // 从当前真实Part authoring替换所属输入动作；其他模块、读取页及功能位置仍由正式reader提供。
    private static ModuleCapsuleCandidate WithInput(ModuleCapsuleCandidate candidate, string operation, string sourceKey, string[] actions)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "modules", candidate.ModuleId);
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var resource = candidate.ModuleId == "Workspace" ? "workspace.folder" : "part.part";
        var list = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == resource + ".list")["view"]!;
        foreach (var surface in operation == "authorize" ? new[] { "rowActions" } : new[] { "pageActions", "rowActions", "featureActions" })
            foreach (var action in list[surface]!.AsArray().OfType<JsonObject>().Where(action =>
                action["handlerKey"]?.GetValue<string>() == operation || action["behaviorKey"]?.GetValue<string>() == resource + "." + operation))
            {
                action["inputReadSourceKey"] = sourceKey;
                action["dataAccess"] = candidate.ModuleId == "Workspace"
                    ? JsonSerializer.SerializeToNode(new[] { "WorkspaceFolder", "Part", "Document" }.Select(name => new Dictionary<string, object> { ["object"] = name, ["actions"] = actions }))
                    : new JsonObject { ["object"] = "Part", ["actions"] = JsonSerializer.SerializeToNode(actions) };
            }
        using var capsule = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "plugin.json")));
        var features = StandardFeatureMetadataCompiler.Read(candidate.ModuleId, documents.Select(document => JsonSerializer.SerializeToElement(document)),
            FeatureCatalogCompiler.Read(capsule.RootElement, candidate.ModuleId), includeCustomFeatures: true);
        return candidate with { Features = features };
    }
}
