using ICP.Platform.Organization.Contracts;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationQueryTests
{
    [Fact]
    public void Contracts_AreIndependentOfImplementationAndDocumentEveryQueryMethod()
    {
        var assembly = typeof(IOrganizationQuery).Assembly;
        Assert.Equal("Organization.Contracts", assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("ICP.")
            || reference.Name.Contains("EntityFramework") || reference.Name.EndsWith("Module"));
        Assert.False(typeof(IOrganizationQuery).Assembly == typeof(OrganizationQuery).Assembly);
        var documentation = File.ReadAllText(Path.ChangeExtension(assembly.Location, ".xml"));
        foreach (var type in new[] { typeof(IOrganizationQuery), typeof(IOrganizationReadView) })
            foreach (var method in type.GetMethods())
                Assert.Contains($"M:{type.FullName}.{method.Name}", documentation);
    }

    [Fact]
    public async Task Read_ReturnsCompleteImmutableCurrentAffiliationAndNamesFromOneSnapshot()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        var definitions = OrganizationModelDefinitions.All.Select(d => d with { OwnerModuleId = "Organization" }).ToArray();
        var services = new ServiceCollection();
        services.AddScoped(_ => new EfPlatformDbContext(options, definitions));
        await using var provider = services.BuildServiceProvider();
        await using var db = new EfPlatformDbContext(options, definitions);
        db.Set<Dictionary<string, object?>>("Company").Add(new()
        { ["EID"] = "c", ["ECODE"] = "C", ["ENAME"] = "公司", ["SortOrder"] = "01", ["IsEnabled"] = true });
        foreach (var (id, parent) in new[] { ("root", (string?)null), ("middle", "root"), ("leaf", "middle") })
            db.Set<Dictionary<string, object?>>("Department").Add(new()
            { ["EID"] = id, ["ECODE"] = id, ["ENAME"] = id, ["CompanyId"] = "c", ["ParentDepartmentId"] = parent,
                ["SortOrder"] = "01", ["IsEnabled"] = id != "middle" });
        db.Set<Dictionary<string, object?>>("Person").Add(new()
        { ["EID"] = "p", ["PersonNo"] = "P", ["ENAME"] = "人员", ["EmploymentStatus"] = "Active", ["IsEnabled"] = true,
            ["CompanyId"] = "c", ["DepartmentId"] = "leaf", ["AccountEID"] = "account", ["BirthDate"] = new DateOnly(1990, 2, 3) });
        await db.SaveChangesAsync();
        var cache = new OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        IOrganizationQuery query = new OrganizationQuery(cache);
        var view = await query.ReadAsync();
        var current = Assert.IsType<CurrentOrganization>(view.FindByAccount("account"));
        Assert.Equal(new DateOnly(1990, 2, 3), current.Person["BirthDate"]);
        Assert.Equal("c", current.Company!["EID"]);
        Assert.Equal("leaf", current.Department!["EID"]);
        Assert.Equal(new[] { "middle", "root" }, current.Ancestors.Select(r => r["EID"]));
        Assert.Equal(false, current.Ancestors[0]["IsEnabled"]);
        Assert.Same(current.Person, view.Find(OrganizationRecordKind.Person, "p"));
        Assert.Null(view.FindByAccount("unknown"));
        Assert.Null(view.Find(OrganizationRecordKind.Person, "unknown"));
        Assert.Equal("公司", Assert.Single(view.ResolveNames(OrganizationRecordKind.Company, ["c", "c", "missing"])).Value.Name);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)current.Person).Add("intrusion", true));
        Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyDictionary<string, object?>>)current.Ancestors).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, OrganizationName>)view.ResolveNames(OrganizationRecordKind.Company, ["c"])).Clear());
    }
}
