using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modeling.Metadata;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Part;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Planning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipRuntimeMetadataTests
{
    [Theory]
    [InlineData("Part")]
    [InlineData("Document")]
    public void RuntimeRegistration_WithoutCandidateAuthoring_RejectsInsteadOfReadingRepository(string module)
    {
        var models = new PartModelMetadataProvider().GetDefinitions().Concat(new DocumentModelMetadataProvider().GetDefinitions()).ToArray();
        var services = new ServiceCollection();
        services.AddSingleton(new CompiledPersistencePlan("candidate", "snapshot", "hash", PersistenceProvider.SqlServer,
            "digest", [], models, []));
        if (module == "Part") new PartModule().ConfigureServices(services);
        else new DocumentModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetServices<StandardCrudDescriptor>().ToArray());

        Assert.Contains("MODEL_AUTHORING_NOT_FOUND", error.Message);
    }

    [Theory]
    [InlineData("Part")]
    [InlineData("Document")]
    public void RuntimeRegistration_UsesCandidateFormRulesEvenWhenRepositoryHasDifferentDefaults(string module)
    {
        var models = new PartModelMetadataProvider().GetDefinitions().Concat(new DocumentModelMetadataProvider().GetDefinitions()).ToArray();
        var source = module == "Part" ? new PartModelMetadataProvider().GetDocument() : new DocumentModelMetadataProvider().GetDocument();
        var json = JsonSerializer.SerializeToNode(source)!;
        var description = json["Model"]!["Fields"]!.AsArray().Single(field => field!["Name"]!.GetValue<string>() == "Description")!;
        description["Form"] = new JsonObject { ["Create"] = new JsonObject { ["Required"] = false, ["DefaultValue"] = "selected-candidate-only" } };
        var selected = json.Deserialize<ModelMetadataDocument>()!;
        var services = new ServiceCollection();
        services.AddSingleton(new CompiledPersistencePlan("candidate", "snapshot", "hash", PersistenceProvider.SqlServer,
            "digest", [], models, []) { ModelAuthoring = new Dictionary<string, ModelMetadataDocument> { [module] = selected } });
        if (module == "Part") new PartModule().ConfigureServices(services);
        else new DocumentModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var descriptor = Assert.Single(provider.GetServices<StandardCrudDescriptor>());

        var policy = Assert.Single(descriptor.FieldPolicies, policy => policy.Field == "Description");
        Assert.Equal("selected-candidate-only", policy.CreateDefaultValue);
        Assert.False(policy.CreateInputRequired);
        Assert.Equal(StandardCrudFieldValueOwner.Server, Assert.Single(descriptor.FieldPolicies, policy => policy.Field == "OwnerId").ValueOwner);
        Assert.Equal(module, descriptor.ModuleId);
    }

    [Fact]
    public void FactoryRegistration_StillRejectsInvalidDescriptorAndDuplicateResource()
    {
        var descriptor = PartCrudDescriptor.Create();
        var invalidServices = new ServiceCollection();
        invalidServices.AddStandardCrudResource(_ => descriptor with { ResourceKey = "" });
        using var invalid = invalidServices.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => invalid.GetServices<StandardCrudDescriptor>().ToArray());

        var duplicateServices = new ServiceCollection();
        duplicateServices.AddStandardCrudResource(_ => descriptor);
        duplicateServices.AddStandardCrudResource(_ => descriptor);
        using var duplicate = duplicateServices.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => duplicate.GetRequiredService<IStandardCrudResourceRegistry>());
    }
}
