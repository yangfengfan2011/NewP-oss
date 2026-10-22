using ICP.Platform.Testing;
using System.Linq.Expressions;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeResourceIdentityTests
{
    [Fact]
    public void ConditionRegistration_RejectsDuplicateAndUnknownTargets()
    {
        var models = new OrganizationModelMetadataProvider().GetDefinitions().Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer("Server=localhost;Database=never-connect;Integrated Security=true;TrustServerCertificate=true")
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options, models);
        var resources = new StandardCrudResourceRegistry([PersonCrudDescriptorFactory.Create()]);
        var plan = new CanonicalModulePlan("test", "1.0", "", "", [], []);
        var identities = new DataScopeIdentities([]);
        Assert.Throws<InvalidOperationException>(() => new DataScopeCatalog(resources, plan, db, identities,
            conditionProviders: [new TestCondition("Person"), new TestCondition("Person")]).BusinessConditions);
        Assert.Throws<InvalidOperationException>(() => new DataScopeCatalog(resources, plan, db, identities,
            conditionProviders: [new TestCondition("Missing/Person")]).BusinessConditions);
    }

    [Fact]
    public void Catalog_ResolvesGlobalModelAndRejectsResourceOwnerMismatch()
    {
        var models = new OrganizationModelMetadataProvider().GetDefinitions().Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
        var person = models.Single(model => model.Name == "Person");
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer("Server=localhost;Database=never-connect;Integrated Security=true;TrustServerCertificate=true")
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options,
            [.. models, person with { Name = "OtherPerson", OwnerModuleId = "Other", TableName = "OtherPerson", ForeignKeys = [] }]);
        Assert.Equal("Person", db.ResolveModelKey("Person"));
        Assert.Throws<InvalidOperationException>(() => db.ResolveModelKey("Organization/Person"));
        var resources = new StandardCrudResourceRegistry([PersonCrudDescriptorFactory.Create()]);
        var catalog = new DataScopeCatalog(resources, new CanonicalModulePlan("test", "1.0", "", "", [], []), db, new DataScopeIdentities([]));
        Assert.Contains(catalog.Fields("organization.person"), field => field.Name == "ENAME");
        Assert.Equal("Person", catalog.Resolve("organization.person").ModelKey);
        var wrongResources = new StandardCrudResourceRegistry([PersonCrudDescriptorFactory.Create() with { ModuleId = "Other" }]);
        var wrongCatalog = new DataScopeCatalog(wrongResources, new CanonicalModulePlan("test", "1.0", "", "", [], []), db, new DataScopeIdentities([]));
        Assert.Equal("DATA_SCOPE_MODEL_OWNER_MISMATCH", Assert.Throws<InvalidOperationException>(() => wrongCatalog.Fields("organization.person")).Message);
    }

    [Fact]
    public void Catalog_KeepsResourceIdentitySeparateFromModelAndValidatesMultiModelConditions()
    {
        var models = new OrganizationModelMetadataProvider().GetDefinitions()
            .Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer("Server=localhost;Database=never-connect;Integrated Security=true;TrustServerCertificate=true")
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options, models);
        var person = PersonCrudDescriptorFactory.Create();
        var resources = new StandardCrudResourceRegistry([
            CompanyCrudDescriptorFactory.Create(), DepartmentCrudDescriptorFactory.Create(),
            PositionCrudDescriptorFactory.Create(), person,
            person with { ResourceKey = "organization.person.other-entry", Route = "api/organization/person-other-entry" }
        ]);
        var catalog = new DataScopeCatalog(resources, new CanonicalModulePlan("test", "1.0", "", "", [], []),
            db, new DataScopeIdentities([]), conditionProviders: [new TestCondition("Company", "Department")]);
        Assert.Equal("Person", catalog.Resolve("organization.person").ModelKey);
        Assert.Equal("Person", catalog.Resolve("organization.person.other-entry").ModelKey);
        Assert.NotEqual(catalog.Resolve("organization.person").ResourceKey,
            catalog.Resolve("organization.person.other-entry").ResourceKey);
        foreach (var resource in new[] { "organization.company", "organization.department" })
            catalog.Validate(new DataScopeRuleDto("Multi_" + resource, "Multi_" + resource, null,
                "多模型条件", true, resource, new("business", "", "", JsonSerializer.SerializeToElement<object?>(null), Key: "test.condition"), [], []));
        Assert.Throws<RolePermissionValidationException>(() => catalog.Validate(new DataScopeRuleDto(
            "Multi_Position", "Multi_Position", null, "非法模型", true, "organization.position",
            new("business", "", "", JsonSerializer.SerializeToElement<object?>(null), Key: "test.condition"), [], [])));
    }

    [Fact]
    [Trait("Category", "ModuleMetadataConsumer")]
    public async Task DirectSubjectCatalog_UsesActualEntityFieldsBeyondTheThreeInitialConsumers()
    {
        var models = new OrganizationModelMetadataProvider().GetDefinitions().Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer("Server=localhost;Database=never-connect;Integrated Security=true;TrustServerCertificate=true")
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options, models);
        var descriptor = CompanyCrudDescriptorFactory.Create();
        var resources = new StandardCrudResourceRegistry([descriptor, descriptor with { ResourceKey = "organization.company.alternate", Route = "api/organization/company-alternate" }]);
        var identities = new DataScopeIdentities([]);
        var catalog = new DataScopeCatalog(resources, new CanonicalModulePlan("test", "1.0", "", "", [], []), db, identities);
        var queries = new DataScopeObjectPermissionQueries(new StandardCrudTestAccess(objects: new ReadableAccess()), identities, null!, new Microsoft.AspNetCore.Http.HttpContextAccessor(), catalog, db);
        foreach (var key in new[] { "organization.company", "organization.company.alternate" })
        {
            var result = await queries.CatalogAsync(key, "catalog-only-target", [new("test.catalog", "test.backend", "read")]);
            Assert.NotNull(result);
            Assert.Equal(new[] { "creator" }, result.SubjectTypes.Select(type => type.Key));
            Assert.False(Assert.Single(result.SubjectTypes).RequiresId);
        }
        Assert.Empty(identities.SubjectTypes);
    }

    private sealed class ReadableAccess : IDataScopeObjectAccess
    {
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default) => Task.FromResult(true);
    }

    private sealed class TestCondition(params string[] models) : IDataScopeBusinessConditionProvider
    {
        public IReadOnlyList<DataScopeBusinessConditionDefinition> Definitions { get; } = [new("test.condition", "测试", models, true)];
        public void Validate(string key, JsonElement value) { }
        public Task<bool> MatchesAsync(string key, DataScopeResource resource, string objectId, JsonElement value,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> MatchesCandidateAsync(string key, DataScopeResource resource,
            IReadOnlyDictionary<string, object?> candidate, JsonElement value,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public Expression BuildReadExpression(string key, DataScopeResource resource, ParameterExpression row, JsonElement value) => Expression.Constant(true);
    }
}
