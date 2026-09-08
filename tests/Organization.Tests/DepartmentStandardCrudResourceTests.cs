using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Crud;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentStandardCrudResourceTests
{
    [Fact]
    public void ExplicitGlobalScope_PreservesUserFiltersButRejectsEmptyPartialAndMixedScopes()
    {
        var customizer = new DepartmentCrudQueryCustomizer();
        var descriptor = OrganizationCrudTestResources.Resolve(DepartmentCrudDescriptorFactory.Create());
        var conditions = customizer.BuildConditions(descriptor, new StandardCrudListQuery(
            Filters: new Dictionary<string, string?> { ["companyId"] = "Chosen" },
            QueryScope: new Dictionary<string, string?> { ["kind"] = "global" }));
        Assert.Equal([new StandardCrudQueryCondition("CompanyId", "=", "Chosen")], conditions);
        foreach (var invalid in new Dictionary<string, string?>[]
        {
            new(), new() { ["CompanyId"] = "Chosen" }, new() { ["ParentDepartmentId"] = null },
            new() { ["kind"] = "global", ["CompanyId"] = "Chosen" }, new() { ["kind"] = "unknown" }
        })
            Assert.Throws<Microsoft.AspNetCore.Http.BadHttpRequestException>(() => customizer.BuildConditions(descriptor, new StandardCrudListQuery(QueryScope: invalid)));
    }

    [Fact]
    public void QueryScope_IntersectsUserFiltersAndPreservesExplicitRoot()
    {
        var conditions = new DepartmentCrudQueryCustomizer().BuildConditions(
            OrganizationCrudTestResources.Resolve(DepartmentCrudDescriptorFactory.Create()),
            new StandardCrudListQuery(Filters: new Dictionary<string, string?> { ["companyId"] = "Other" },
                QueryScope: new Dictionary<string, string?> { ["CompanyId"] = "Current", ["ParentDepartmentId"] = null }));
        Assert.Contains(new StandardCrudQueryCondition("CompanyId", "=", "Other"), conditions);
        Assert.Contains(new StandardCrudQueryCondition("CompanyId", "=", "Current"), conditions);
        Assert.Contains(new StandardCrudQueryCondition("ParentDepartmentId", "isNull", null), conditions);
    }

    [Fact]
    public void Descriptor_ExposesHierarchyFieldsStableSortAndConditionalDeleteWithoutRevision()
    {
        var descriptor = OrganizationCrudTestResources.Resolve(DepartmentCrudDescriptorFactory.Create());

        StandardCrudDescriptorValidator.Validate(descriptor);

        Assert.Equal("organization.department", descriptor.ResourceKey);
        Assert.Equal("api/organization/departments", descriptor.Route);
        Assert.Equal(
            ["EID", "CompanyId", "ParentDepartmentId", "ECODE", "ENAME", "IsEnabled", "SortOrder", "UpdatedAt"],
            descriptor.FieldSet.ListFields);
        Assert.Equal(["CompanyId", "ParentDepartmentId", "ECODE", "ENAME", "SortOrder"], descriptor.FieldSet.CreateFields);
        Assert.Equal(["ParentDepartmentId", "ENAME", "SortOrder", "IsEnabled"], descriptor.FieldSet.UpdateFields);
        Assert.Equal("SortOrder", descriptor.DefaultSortField);
        Assert.Equal(["ECODE"], descriptor.StableSortFields);
        Assert.Null(descriptor.ConcurrencyRevisionField);
        Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
        var authorization = Assert.IsType<StandardCrudAuthorization>(descriptor.Authorization);
        Assert.Equal("Organization.Department.View", authorization.GetPermissionCode(StandardCrudOperation.List));
        Assert.Equal("Organization.Department.Create", authorization.GetPermissionCode(StandardCrudOperation.Create));
        Assert.Equal("Organization.Department.Update", authorization.GetPermissionCode(StandardCrudOperation.Update));
        Assert.Equal("Organization.Department.Delete", authorization.GetPermissionCode(StandardCrudOperation.Delete));

        Assert.Collection(
            descriptor.UniqueConstraints,
            constraint =>
            {
                Assert.Equal(["ECODE"], constraint.Fields);
                Assert.Equal(["CompanyId"], constraint.ScopeFields);
            },
            constraint =>
            {
                Assert.Equal(["ENAME"], constraint.Fields);
                Assert.Equal(["CompanyId", "ParentDepartmentId"], constraint.ScopeFields);
            });
        Assert.All(descriptor.UniqueConstraints, constraint =>
        {
            Assert.True(constraint.CaseInsensitive);
            Assert.True(constraint.TrimWhitespace);
        });

        var enabled = Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "IsEnabled");
        Assert.Equal(StandardCrudFieldValueOwner.ServerOnCreate, enabled.ValueOwner);
        Assert.Equal(true, enabled.CreateDefaultValue);
        Assert.Equal(
            StandardCrudFieldValueOwner.CallerOnCreate,
            Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "CompanyId").ValueOwner);
        Assert.Equal(
            StandardCrudFieldValueOwner.CallerOnCreate,
            Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "ECODE").ValueOwner);
        var parent = Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "ParentDepartmentId");
        Assert.Equal(StandardCrudFieldValueOwner.Caller, parent.ValueOwner);
        Assert.False(parent.StorageRequired);

        Assert.Collection(
            descriptor.DisplayValueFields,
            field =>
            {
                Assert.Equal("CompanyId_DisplayValue", field.Field);
                Assert.Equal("CompanyId", field.SourceField);
            },
            field =>
            {
                Assert.Equal("ParentDepartmentId_DisplayValue", field.Field);
                Assert.Equal("ParentDepartmentId", field.SourceField);
            });
        Assert.NotNull(descriptor.DisplayProjectionPlan);
    }

    [Fact]
    public void OrganizationModule_RegistersDepartmentAsTheOnlyStandardCrudRouteOwner()
    {
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IStandardCrudResourceRegistry>();
        var descriptor = registry.FindByResourceKey("organization.department");

        Assert.NotNull(descriptor);
        Assert.Same(descriptor, registry.FindByRoute("api/organization/departments"));
        Assert.DoesNotContain(
            typeof(OrganizationModule).Assembly.GetTypes(),
            type => string.Equals(type.Name, "DepartmentsController", StringComparison.Ordinal));
    }

    [Fact]
    public void QueryCustomizer_SearchesCodeOrNameAndFiltersCompanyParentAndStatus()
    {
        var conditions = new DepartmentCrudQueryCustomizer().BuildConditions(
            OrganizationCrudTestResources.Resolve(DepartmentCrudDescriptorFactory.Create()),
            new StandardCrudListQuery(1, 20, null, null, new Dictionary<string, string?>
            {
                ["keyword"] = "研发",
                ["companyId"] = "Company_1",
                ["parentDepartmentId"] = "Department_Parent",
                ["isEnabled"] = "false"
            }));

        Assert.Collection(
            conditions,
            condition => Assert.Equal(new StandardCrudQueryCondition("ECODE", "contains", "研发", "keyword", "or"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("ENAME", "contains", "研发", "keyword", "or"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("CompanyId", "=", "Company_1"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("ParentDepartmentId", "=", "Department_Parent"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("IsEnabled", "=", "false"), condition));
    }
}
