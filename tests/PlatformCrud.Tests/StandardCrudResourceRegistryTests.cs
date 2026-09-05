using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudResourceRegistryTests
{
    [Fact]
    public void Constructor_WithCanonicalModels_AutomaticallyAdoptsAuditPlanWithinReadFields()
    {
        var model = new ICP.Platform.Modeling.Projection.ModelMetadataProjector().Project(
            new ICP.Platform.Modeling.Metadata.ModelMetadataDocument
            {
                ModuleId = "Organization",
                Model = new ICP.Platform.Modeling.Metadata.ModelMetadataModel { Name = "Person", Kind = "Entity" }
            }) with { OwnerModuleId = "Organization" };
        var persistence = new ICP.Platform.Persistence.Planning.CompiledPersistencePlan(
            "candidate", "snapshot", "hash", default, "digest", [], [model], []);
        var descriptor = Descriptor() with
        {
            FieldSet = new StandardCrudFieldSet(["EID", "UpdatedAt"],
                ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"], [], [])
        };

        var registered = Assert.Single(new StandardCrudResourceRegistry([descriptor], persistence).GetAll());

        Assert.Equal(4, registered.DisplayProjectionPlan!.Fields.Count);
        var query = StandardCrudQueryDescriptorAdapter.ToQueryDescriptor(registered);
        Assert.Equal("UpdatedAt_DisplayValue", Assert.Single(query.DisplayValueFields).Field);
        Assert.DoesNotContain("CreatedBy", query.DefaultFields);
    }

    [Fact]
    public void GetAll_ReturnsRegisteredDescriptors()
    {
        var descriptor = Descriptor();
        var registry = new StandardCrudResourceRegistry([descriptor]);

        var registered = Assert.Single(registry.GetAll());

        Assert.Same(descriptor, registered);
        Assert.Same(descriptor, registry.FindByResourceKey("organization.person"));
        Assert.Same(descriptor, registry.FindByRoute("api/organization/persons"));
        Assert.Same(descriptor, registry.FindByRoute("/api/organization/persons/"));
    }

    [Fact]
    public void Constructor_WhenResourceKeyDuplicated_ThrowsStableMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StandardCrudResourceRegistry(
            [
                Descriptor(),
                Descriptor(route: "api/organization/persons-copy")
            ]));

        Assert.Equal("标准 CRUD 资源 organization.person 重复注册。", ex.Message);
    }

    [Fact]
    public void Constructor_WhenRouteDuplicated_ThrowsStableMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StandardCrudResourceRegistry(
            [
                Descriptor(resourceKey: "organization.person"),
                Descriptor(resourceKey: "organization.employee", route: "/api/organization/persons/")
            ]));

        Assert.Equal("标准 CRUD 路由 api/organization/persons 被 organization.person 与 organization.employee 重复声明。", ex.Message);
    }

    [Fact]
    public void GetAll_DoesNotExposeMutableDescriptorList()
    {
        var registry = new StandardCrudResourceRegistry([Descriptor()]);
        var registered = registry.GetAll();

        Assert.False(registered is List<StandardCrudDescriptor>);
        Assert.False(registered is StandardCrudDescriptor[]);
    }

    private static StandardCrudDescriptor Descriptor(
        string resourceKey = "organization.person",
        string route = "api/organization/persons") => new()
    {
        ResourceKey = resourceKey,
        ModuleId = "Organization",
        ModelName = "Person",
        Route = route,
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME"],
            DetailFields: ["EID", "PersonNo", "ENAME"],
            CreateFields: ["PersonNo", "ENAME"],
            UpdateFields: ["ENAME"]),
        DeletePolicy = StandardCrudDeletePolicy.None
    };
}
