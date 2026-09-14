using ICP.Platform.Authorization.Http;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;
namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureEndpointBindingsTests
{
    [Fact]
    public void Validate_RejectsMissingDeclarationMissingConsumerAndWrongOwner()
    {
        var catalog = FeatureCatalogCompiler.Compile([new("Authorization", "entry", "navigation", "授权", null, "authorization.read")]);
        var endpoint = new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EndpointModuleAttribute("Authorization"), new RequireFeatureAttribute("authorization.read")), "read");
        FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(endpoint)], catalog);
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([], catalog));
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(endpoint)], []));
        var wrong = new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EndpointModuleAttribute("Other"), new RequireFeatureAttribute("authorization.read")), "wrong");
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(wrong)], catalog));
    }
    [Fact]
    public void Validate_RequiresEveryAdditionalReadBindingAndRejectsWriteEndpointsAsReads()
    {
        var catalog = FeatureCatalogCompiler.Compile([new("Organization", "persons", "navigation", "人员", null, "person.read")
            { ReadBehaviors = ["person.lookup"] }]);
        Endpoint Read(string behavior, string method) => new(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EndpointModuleAttribute("Organization"), new RequireFeatureAttribute(behavior), new HttpMethodMetadata([method])), behavior);
        var main = Read("person.read", "GET");
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(main)], catalog));
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(main, Read("person.lookup", "POST"))], catalog));
        FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(main, Read("person.lookup", "GET"))], catalog);
    }
}
