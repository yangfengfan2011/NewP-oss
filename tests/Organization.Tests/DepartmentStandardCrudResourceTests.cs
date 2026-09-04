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
    public void Descriptor_ExposesRootDepartmentFieldsStableSortAndConditionalDeleteWithoutRevision()
    {
        var descriptor = DepartmentCrudDescriptorFactory.Create();

        StandardCrudDescriptorValidator.Validate(descriptor);

        Assert.Equal("organization.department", descriptor.ResourceKey);
        Assert.Equal("api/organization/departments", descriptor.Route);
        Assert.Equal(
            ["EID", "CompanyId", "ECODE", "ENAME", "IsEnabled", "SortOrder", "UpdatedAt"],
            descriptor.FieldSet.ListFields);
        Assert.Equal(["CompanyId", "ECODE", "ENAME", "SortOrder"], descriptor.FieldSet.CreateFields);
        Assert.Equal(["ENAME", "SortOrder", "IsEnabled"], descriptor.FieldSet.UpdateFields);
        Assert.DoesNotContain("ParentDepartmentId", descriptor.FieldSet.CreateFields);
        Assert.DoesNotContain("ParentDepartmentId", descriptor.FieldSet.UpdateFields);
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
            constraint => Assert.Equal(["CompanyId", "ECODE"], constraint.Fields),
            constraint => Assert.Equal(["CompanyId", "ENAME"], constraint.Fields));
        Assert.All(descriptor.UniqueConstraints, constraint =>
        {
            Assert.True(constraint.CaseInsensitive);
            Assert.True(constraint.TrimWhitespace);
        });

        var enabled = Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "IsEnabled");
        Assert.Equal(StandardCrudFieldValueOwner.ServerOnCreate, enabled.ValueOwner);
        Assert.Equal(true, enabled.CreateDefaultValue);
        var parent = Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "ParentDepartmentId");
        Assert.Equal(StandardCrudFieldValueOwner.ServerOnCreate, parent.ValueOwner);
        Assert.Null(parent.CreateDefaultValue);
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
    public void QueryCustomizer_SearchesCodeOrNameAndFiltersCompanyAndStatus()
    {
        var conditions = new DepartmentCrudQueryCustomizer().BuildConditions(
            DepartmentCrudDescriptorFactory.Create(),
            new StandardCrudListQuery(1, 20, null, null, new Dictionary<string, string?>
            {
                ["keyword"] = "研发",
                ["companyId"] = "Company_1",
                ["isEnabled"] = "false"
            }));

        Assert.Collection(
            conditions,
            condition => Assert.Equal(new StandardCrudQueryCondition("ECODE", "contains", "研发", "keyword", "or"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("ENAME", "contains", "研发", "keyword", "or"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("CompanyId", "=", "Company_1"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("IsEnabled", "=", "false"), condition));
    }
}
