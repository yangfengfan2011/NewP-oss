using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using Xunit;
namespace ICP.Platform.Modules.Authorization.Tests;
public sealed class FeaturePersistenceTests
{
    [Fact]
    public void AuthoringProvidesFullKeyAndBidirectionalRoleLinks()
    {
        var definitions = new AuthorizationModelMetadataProvider().GetDefinitions();
        var point = Assert.Single(definitions, model => model.Name == "FunctionPoint");
        Assert.True(Assert.Single(point.Fields, field => field.Name == "PositionKey").OrdinalKey);
        var grants = Assert.Single(definitions, model => model.Name == "RoleFunctionGrant");
        Assert.Contains(grants.Indexes, index => index.IsUnique && index.Fields.SequenceEqual(["RoleId", "FunctionPointId"]));
        Assert.Contains(grants.Indexes, index => index.Fields.SequenceEqual(["FunctionPointId", "RoleId"]));
    }
}
