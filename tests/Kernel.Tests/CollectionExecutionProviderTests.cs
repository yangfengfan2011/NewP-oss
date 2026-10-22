using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class CollectionExecutionProviderTests
{
    [Fact]
    public void ExecutionOwner_BindsOnlyTheShortResourceSetToTheProviderFloor()
    {
        var contracts = Read("\"resources\":[\"folder\",\"part\"]");
        var provider = Provider("execution-owner");
        var resolved = Assert.Single(DataExecutionProviders.Resolve(contracts, [provider]));
        Assert.Equal("Authorization", resolved.ProviderModuleId);
        var role = Assert.Single(resolved.Requirements);
        Assert.Equal(new[] { "folder", "part" }, role.Resources);
        Assert.Equal(new[] { "read" }, Assert.Single(role.AnyOf));
        Assert.Equal(("object", 1, (int?)1), (role.Shape, role.MinimumCount, role.MaximumCount));
    }

    [Theory]
    [InlineData("\"resources\":[]")]
    [InlineData("\"resources\":[\"folder\",\"folder\"]")]
    [InlineData("\"resources\":[\"folder\"],\"resourceKey\":\"folder\"")]
    [InlineData("\"resources\":[\"folder\"],\"requirements\":[]")]
    [InlineData("\"resources\":[\"folder\"],\"legacyActions\":true")]
    [InlineData("\"resources\":[\"folder\"],\"branches\":[\"skip\"]")]
    [InlineData("\"targetResources\":[\"folder\"]")]
    public void ExecutionOwner_RejectsConsumerFloorAndResourceRewrites(string properties) =>
        Assert.Throws<InvalidOperationException>(() => DataExecutionProviders.Resolve(Read(properties), [Provider("execution-owner")]));

    [Fact]
    public void ResourceOwner_CannotBeChangedIntoACollectionConsumer() =>
        Assert.Throws<InvalidOperationException>(() => DataExecutionProviders.Resolve(Read("\"resources\":[\"folder\",\"part\"]"), [Provider("resource-owner")]));

    private static IReadOnlyList<DataExecutionContract> Read(string properties)
    {
        using var capsule = JsonDocument.Parse("""{"dataExecutions":[{"key":"workspace.permissions","moduleId":"Workspace","provider":"authorization.collection.view","boundary":"entry","featureBehaviors":["workspace.folder.authorize"],"""
            + properties + "}]}");
        return DataExecutionContracts.Read(capsule.RootElement, "Workspace");
    }

    private static DataExecutionProviderContract Provider(string binding) => new("authorization.collection.view", "Authorization", "authorization.backend", binding,
        [new("view", "prepare", "object", ["$resource"], [["read"]])]);
}
