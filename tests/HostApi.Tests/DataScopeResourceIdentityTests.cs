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
            conditionProviders: [new TestCondition("Organization/Person"), new TestCondition("Organization/Person")]).BusinessConditions);
        Assert.Throws<InvalidOperationException>(() => new DataScopeCatalog(resources, plan, db, identities,
            conditionProviders: [new TestCondition("Missing/Person")]).BusinessConditions);
    }

    [Theory]
    [InlineData("Person")]
    [InlineData("Organization/Person")]
    public void Catalog_UsesDeclaredOwnerWhenBareModelNameIsAmbiguous(string modelName)
    {
        var models = new OrganizationModelMetadataProvider().GetDefinitions().Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
        var person = models.Single(model => model.Name == "Person");
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer("Server=localhost;Database=never-connect;Integrated Security=true;TrustServerCertificate=true")
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options,
            [.. models, person with { OwnerModuleId = "Other", TableName = "OtherPerson", ForeignKeys = [] }]);
        Assert.Throws<InvalidOperationException>(() => db.ResolveModelKey("Person"));
        var resources = new StandardCrudResourceRegistry([PersonCrudDescriptorFactory.Create() with { ModelName = modelName }]);
        var catalog = new DataScopeCatalog(resources, new CanonicalModulePlan("test", "1.0", "", "", [], []), db, new DataScopeIdentities([]));
        Assert.Contains(catalog.Fields("organization.person"), field => field.Name == "ENAME");
        Assert.Equal("Organization/Person", catalog.Resolve("organization.person").ModelKey);
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
            db, new DataScopeIdentities([]), conditionProviders: [new TestCondition("Organization/Company", "Organization/Department")]);
        Assert.Equal("Organization/Person", catalog.Resolve("organization.person").ModelKey);
        Assert.Equal("Organization/Person", catalog.Resolve("organization.person.other-entry").ModelKey);
        Assert.NotEqual(catalog.Resolve("organization.person").ResourceKey,
            catalog.Resolve("organization.person.other-entry").ResourceKey);
        foreach (var resource in new[] { "organization.company", "organization.department" })
            catalog.Validate(new DataScopeRuleDto("Multi_" + resource, "Multi_" + resource, null,
                "多模型条件", true, resource, new("business", "", "", JsonSerializer.SerializeToElement<object?>(null), Key: "test.condition"), [], []));
        Assert.Throws<RolePermissionValidationException>(() => catalog.Validate(new DataScopeRuleDto(
            "Multi_Position", "Multi_Position", null, "非法模型", true, "organization.position",
            new("business", "", "", JsonSerializer.SerializeToElement<object?>(null), Key: "test.condition"), [], [])));
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
