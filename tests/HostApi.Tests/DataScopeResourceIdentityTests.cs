using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeResourceIdentityTests
{
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
        var catalog = new DataScopeCatalog(resources, new CanonicalModulePlan("test", "1.0", "", "", [], []), db);
        Assert.Contains(catalog.Fields("organization.person"), field => field.Name == "ENAME");
        Assert.Equal("Organization/Person", catalog.Resolve("organization.person").ModelKey);
    }
}
