using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Crud;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class CompanyStandardCrudResourceTests
{
    [Fact]
    public void Descriptor_ExposesCompanyFieldsStableSortAndConditionalDeleteWithoutRevision()
    {
        var descriptor = CompanyCrudDescriptorFactory.Create();

        StandardCrudDescriptorValidator.Validate(descriptor);

        Assert.Equal("organization.company", descriptor.ResourceKey);
        Assert.Equal("api/organization/companies", descriptor.Route);
        Assert.Equal(["EID", "ECODE", "ENAME", "IsEnabled", "SortOrder", "UpdatedAt"], descriptor.FieldSet.ListFields);
        Assert.Equal(["ECODE", "ENAME", "SortOrder"], descriptor.FieldSet.CreateFields);
        Assert.Equal(["ENAME", "SortOrder", "IsEnabled"], descriptor.FieldSet.UpdateFields);
        Assert.Equal("SortOrder", descriptor.DefaultSortField);
        Assert.Equal(["ECODE"], descriptor.StableSortFields);
        Assert.Null(descriptor.ConcurrencyRevisionField);
        Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
        var authorization = Assert.IsType<StandardCrudAuthorization>(descriptor.Authorization);
        Assert.Equal("Organization.Company.View", authorization.GetPermissionCode(StandardCrudOperation.List));
        Assert.Equal("Organization.Company.Create", authorization.GetPermissionCode(StandardCrudOperation.Create));
        Assert.Equal("Organization.Company.Update", authorization.GetPermissionCode(StandardCrudOperation.Update));
        Assert.Equal("Organization.Company.Delete", authorization.GetPermissionCode(StandardCrudOperation.Delete));

        Assert.Collection(
            descriptor.UniqueConstraints,
            constraint =>
            {
                Assert.Equal("code", constraint.Key);
                Assert.Equal(["ECODE"], constraint.Fields);
                Assert.True(constraint.CaseInsensitive);
                Assert.True(constraint.TrimWhitespace);
            },
            constraint =>
            {
                Assert.Equal("name", constraint.Key);
                Assert.Equal(["ENAME"], constraint.Fields);
                Assert.True(constraint.CaseInsensitive);
                Assert.True(constraint.TrimWhitespace);
            });

        var enabled = Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "IsEnabled");
        Assert.Equal(StandardCrudFieldValueOwner.ServerOnCreate, enabled.ValueOwner);
        Assert.Equal(true, enabled.CreateDefaultValue);
    }

    [Fact]
    public void OrganizationModule_RegistersCompanyAsTheOnlyStandardCrudRouteOwner()
    {
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IStandardCrudResourceRegistry>();
        var descriptor = registry.FindByResourceKey("organization.company");

        Assert.NotNull(descriptor);
        Assert.Same(descriptor, registry.FindByRoute("api/organization/companies"));
        Assert.DoesNotContain(
            typeof(OrganizationModule).Assembly.GetTypes(),
            type => string.Equals(type.Name, "CompaniesController", StringComparison.Ordinal));
    }

    [Fact]
    public void QueryCustomizer_SearchesCodeOrNameAndFiltersStatus()
    {
        var conditions = new CompanyCrudQueryCustomizer().BuildConditions(
            CompanyCrudDescriptorFactory.Create(),
            new StandardCrudListQuery(1, 20, null, null, new Dictionary<string, string?>
            {
                ["keyword"] = "平台",
                ["isEnabled"] = "false"
            }));

        Assert.Collection(
            conditions,
            condition => Assert.Equal(new StandardCrudQueryCondition("ECODE", "contains", "平台", "keyword", "or"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("ENAME", "contains", "平台", "keyword", "or"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("IsEnabled", "=", "false"), condition));
    }
}
