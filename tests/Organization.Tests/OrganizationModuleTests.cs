using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationModuleTests
{
    [Fact]
    public void ConfigureServices_RegistersCompanyCommitParticipantAndOneSharedCache()
    {
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<ICP.Platform.Persistence.Transactions.PlatformCommitRegistration>());
        Assert.Equal("Organization/Company", registration.ModelKey);
        Assert.Same(provider.GetRequiredService<ICP.Platform.Modules.Organization.Caching.OrganizationCache>(), registration.Participant);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(ICP.Platform.Modules.Organization.Caching.OrganizationCache)).Lifetime);
    }

    [Fact]
    public void ModuleId_ReturnsOrganization()
    {
        var module = new OrganizationModule();

        Assert.Equal("Organization", module.ModuleId);
    }

    [Fact]
    public void Module_UsesEmbeddedCapsuleAndExposesOnlyStableBackendBehaviorKey()
    {
        var module = new OrganizationModule();

        var binding = Assert.IsAssignableFrom<IModuleBehaviorBinding>(module);
        Assert.IsAssignableFrom<ModuleCapsuleBindingBase>(module);
        Assert.Equal("organization.backend", binding.BehaviorKey);
    }

    [Fact]
    public void CurrentAuthoring_CompilesLifecyclePositionsWithTheirExecutionBehaviors()
    {
        var module = Path.Combine(FindRepositoryRoot(), "modules", "Organization");
        var documents = Directory.GetFiles(Path.Combine(module, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path)));
        var capsule = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(module, "newp.plugin.json")));
        var declared = FeatureCatalogCompiler.Read(capsule, "Organization");
        var points = FeatureCatalogCompiler.Compile(declared
            .Concat(StandardFeatureMetadataCompiler.Read("Organization", documents, declared)));

        foreach (var resource in new[] { "person", "company", "department", "position" })
        {
            foreach (var operation in new[] { "read", "create", "update" })
            {
                Assert.Contains(points, point => point.BehaviorKey == $"organization.{resource}.{operation}");
            }
        }
        Assert.Contains(points, point => point.BehaviorKey == "organization.person.enable");
        Assert.Contains(points, point => point.BehaviorKey == "organization.person.disable");
        Assert.All(points, point => Assert.StartsWith("Organization/", point.Position));
    }

    [Fact]
    public void ConfigureServices_RegistersAuthorizationSummaryReader()
    {
        var services = new ServiceCollection();
        var module = new OrganizationModule();

        module.ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAuthorizationSummaryReader) &&
            descriptor.ImplementationType == typeof(NullAuthorizationSummaryReader));
    }

    [Fact]
    public void ConfigureServices_PairsDeleteGuardAndConditionalDeleteExecutorPerResource()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options, PlatformPersistenceModelCatalog.AdaptPlatformCore(NonSqlPlatformBaselineModelSource.GetDefinitions()).ToArray()));
        new OrganizationModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var guards = scope.ServiceProvider.GetServices<IStandardCrudDeleteGuard>().ToDictionary(guard => guard.ResourceKey);
        var executors = scope.ServiceProvider.GetServices<IStandardCrudConditionalDeleteExecutor>().ToDictionary(executor => executor.ResourceKey);
        var expected = new[] { "organization.company", "organization.department", "organization.position", "organization.person" };
        Assert.Equal(expected.Order(), guards.Keys.Order());
        Assert.Equal(expected.Order(), executors.Keys.Order());
        foreach (var resourceKey in expected)
            Assert.Same(guards[resourceKey], executors[resourceKey]);

        using var other = provider.CreateScope();
        var otherGuard = other.ServiceProvider.GetServices<IStandardCrudDeleteGuard>().Single(guard => guard.ResourceKey == "organization.company");
        Assert.NotSame(guards["organization.company"], otherGuard);
    }

    [Fact]
    public void OrganizationMetadataFiles_UseExpectedTypeHeaders()
    {
        var root = FindRepositoryRoot();

        AssertFileContains(root, @"modules\Organization\metadata\navigation\Organization.nav.json", "\"type\": \"nav\"");
        AssertFileContains(root, @"modules\Organization\metadata\views\PersonList.view.json", "\"type\": \"view\"");
        AssertFileContains(root, @"modules\Organization\metadata\views\PersonDetail.view.json", "\"type\": \"view\"");
        AssertFileContains(root, @"modules\Organization\metadata\views\PositionList.view.json", "\"type\": \"view\"");
        AssertFileContains(root, @"modules\Organization\metadata\views\CompanyList.view.json", "\"type\": \"view\"");
        AssertFileContains(root, @"modules\Organization\metadata\views\DepartmentList.view.json", "\"type\": \"view\"");
        AssertFileContains(root, @"modules\Organization\metadata\models\Person.model.json", "\"type\": \"model\"");
        AssertFileContains(root, @"modules\Organization\metadata\dictionaries\PersonListView.dic.json", "\"type\": \"dic\"");
        AssertFileContains(root, @"modules\Organization\metadata\dictionaries\PersonDetailView.dic.json", "\"type\": \"dic\"");
        AssertFileContains(root, @"modules\Organization\metadata\dictionaries\PositionListView.dic.json", "\"type\": \"dic\"");
        AssertFileContains(root, @"modules\Organization\metadata\dictionaries\CompanyListView.dic.json", "\"type\": \"dic\"");
        AssertFileContains(root, @"modules\Organization\metadata\dictionaries\DepartmentListView.dic.json", "\"type\": \"dic\"");
    }

    [Fact]
    public void PersonnelMetadata_MakesAffiliationOptionalAndGatesWriteActions()
    {
        var root = FindRepositoryRoot();
        using var model = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\models\Person.model.json"),
            Encoding.UTF8));
        var fields = model.RootElement.GetProperty("Person").GetProperty("fields").EnumerateArray().ToArray();

        Assert.False(IsCreateRequired(fields, "CompanyId"));
        Assert.False(IsEditRequired(fields, "CompanyId"));
        Assert.False(IsCreateRequired(fields, "DepartmentId"));
        Assert.False(IsEditRequired(fields, "DepartmentId"));
        Assert.True(IsCreateRequired(fields, "EmploymentStatus"));
        Assert.False(IsCreateRequired(fields, "PositionId"));
        var departmentField = fields.Single(field => field.GetProperty("name").GetString() == "DepartmentId");
        var departmentSource = departmentField.GetProperty("dataSource");
        Assert.Equal("organization.departments", departmentSource.GetProperty("key").GetString());
        Assert.Equal(
            ["CompanyId"],
            departmentSource.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        using var listView = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\views\PersonList.view.json"),
            Encoding.UTF8));
        var actions = listView.RootElement.GetProperty("view").GetProperty("rowActions").EnumerateArray().ToArray();
        Assert.Equal("edit", StandardAction(actions, "edit"));
        Assert.Equal("disable", StandardAction(actions, "disable"));
        Assert.Equal("enable", StandardAction(actions, "enable"));

        var advancedSections = listView.RootElement.GetProperty("view")
            .GetProperty("advancedFilter")
            .GetProperty("sections")
            .GetArrayLength();
        Assert.Equal(2, advancedSections);
        var personSection = listView.RootElement.GetProperty("view")
            .GetProperty("advancedFilter")
            .GetProperty("sections")[0];
        Assert.Equal("person", personSection.GetProperty("key").GetString());
        var birthDateRange = Assert.Single(personSection.GetProperty("fields").EnumerateArray());
        Assert.Equal("BirthDate", birthDateRange.GetProperty("field").GetString());
        Assert.Equal("dateRange", birthDateRange.GetProperty("operator").GetString());
        Assert.Equal("birthDateFrom", birthDateRange.GetProperty("fromKey").GetString());
        Assert.Equal("birthDateTo", birthDateRange.GetProperty("toKey").GetString());

        var advancedFields = listView.RootElement.GetProperty("view")
            .GetProperty("advancedFilter")
            .GetProperty("sections")[1]
            .GetProperty("fields")
            .EnumerateArray()
            .ToArray();
        var positionFilter = advancedFields.Single(field => field.GetProperty("key").GetString() == "positionId");
        Assert.False(positionFilter.TryGetProperty("dependsOn", out _));
        Assert.Equal(
            ["PersonNo", "ENAME", "BirthDate", "CompanyId", "DepartmentName", "PositionName", "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt", "IsEnabled"],
            listView.RootElement.GetProperty("view").GetProperty("columns").EnumerateArray()
                .Select(column => column.GetProperty("field").GetString()));
        Assert.All(
            listView.RootElement.GetProperty("view").GetProperty("columns").EnumerateArray()
                .Where(column => column.GetProperty("field").GetString() is "BirthDate" or "EmploymentStartedAt" or "EmploymentEndedAt"),
            column => Assert.True(column.GetProperty("sortable").GetBoolean()));
    }

    [Fact]
    public void PositionMetadata_UsesViewAndEditDrawersWithSeparatePermissions()
    {
        var root = FindRepositoryRoot();
        using var model = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\models\Position.model.json"),
            Encoding.UTF8));
        var fields = model.RootElement.GetProperty("Position").GetProperty("fields").EnumerateArray().ToArray();

        Assert.Equal("editable", FormMode(fields, "ENAME", "edit"));
        Assert.True(IsEditRequired(fields, "ENAME"));
        Assert.Equal("editable", FormMode(fields, "Description", "edit"));
        Assert.Equal("hidden", FormMode(fields, "IsEnabled", "create"));
        Assert.Equal("editable", FormMode(fields, "IsEnabled", "edit"));
        Assert.Equal(
            ["ENAME", "Description", "IsEnabled"],
            model.RootElement.GetProperty("Position")
                .GetProperty("sections")[0]
                .GetProperty("fields")
                .EnumerateArray()
                .Select(field => field.GetString()));

        using var dictionary = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\dictionaries\PositionListView.dic.json"),
            Encoding.UTF8));
        Assert.Equal(
            "启用状态",
            dictionary.RootElement.GetProperty("PositionListView").GetProperty("Field_IsEnabled").GetProperty("zhs").GetString());

        using var listView = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\views\PositionList.view.json"),
            Encoding.UTF8));
        var actions = listView.RootElement.GetProperty("view").GetProperty("rowActions").EnumerateArray().ToArray();
        Assert.Equal(2, actions.Length);
        var edit = actions.Single(action => action.GetProperty("key").GetString() == "edit");
        Assert.Equal("edit", edit.GetProperty("standardAction").GetString());
        Assert.Equal("edit", StandardAction(actions, "edit"));
        var delete = actions.Single(action => action.GetProperty("key").GetString() == "delete");
        Assert.Equal("delete", delete.GetProperty("standardAction").GetString());
        Assert.Equal("Action_Delete_Confirm", delete.GetProperty("confirmMessageKey").GetString());
        Assert.Equal("delete", StandardAction(actions, "delete"));
        Assert.False(delete.TryGetProperty("visibleWhen", out _));
        Assert.False(delete.TryGetProperty("disabledWhen", out _));
    }

    [Fact]
    public void CompanyMetadata_UsesStandardViewCreateEditAndDeleteWithoutExport()
    {
        var root = FindRepositoryRoot();
        using var model = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\models\Company.model.json"),
            Encoding.UTF8));
        var fields = model.RootElement.GetProperty("Company").GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal("editable", FormMode(fields, "ECODE", "create"));
        Assert.Equal("readonly", FormMode(fields, "ECODE", "edit"));
        Assert.Equal("editable", FormMode(fields, "ENAME", "edit"));
        Assert.Equal("editable", FormMode(fields, "SortOrder", "edit"));
        Assert.Equal("hidden", FormMode(fields, "IsEnabled", "create"));
        Assert.Equal("editable", FormMode(fields, "IsEnabled", "edit"));

        using var listView = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\views\CompanyList.view.json"),
            Encoding.UTF8));
        var view = listView.RootElement.GetProperty("view");
        Assert.Equal("organization.company.list", view.GetProperty("key").GetString());
        Assert.Equal("SortOrder", view.GetProperty("query").GetProperty("defaultSort").GetProperty("field").GetString());
        Assert.Equal(
            ["ECODE", "ENAME", "IsEnabled", "SortOrder", "UpdatedAt"],
            view.GetProperty("columns").EnumerateArray().Select(column => column.GetProperty("field").GetString()));
        Assert.Equal("create", Assert.Single(view.GetProperty("pageActions").EnumerateArray()).GetProperty("standardAction").GetString());
        var rowActions = view.GetProperty("rowActions").EnumerateArray().ToArray();
        Assert.Equal(["edit", "delete"], rowActions.Select(action => action.GetProperty("standardAction").GetString()));
        Assert.Equal("edit", StandardAction(rowActions, "edit"));
        Assert.Equal("delete", StandardAction(rowActions, "delete"));
        Assert.Equal("organization.company.detail", view.GetProperty("detailViewKey").GetString());
        Assert.False(view.TryGetProperty("export", out _));
        Assert.False(view.TryGetProperty("bulkActions", out _));
    }

    [Fact]
    public void DepartmentMetadata_UsesHierarchyListWithDetailAndDependentParentLookupWithoutExportOrBulkActions()
    {
        var root = FindRepositoryRoot();
        using var model = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\models\Department.model.json"),
            Encoding.UTF8));
        var fields = model.RootElement.GetProperty("Department").GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal("editable", FormMode(fields, "CompanyId", "create"));
        Assert.Equal("readonly", FormMode(fields, "CompanyId", "edit"));
        Assert.Equal("editable", FormMode(fields, "ECODE", "create"));
        Assert.Equal("readonly", FormMode(fields, "ECODE", "edit"));
        Assert.Contains(
            "ParentDepartmentId",
            model.RootElement.GetProperty("Department").GetProperty("sections")[0].GetProperty("fields")
                .EnumerateArray().Select(static field => field.GetString()));
        Assert.Equal("editable", FormMode(fields, "ParentDepartmentId", "create"));
        Assert.Equal("editable", FormMode(fields, "ParentDepartmentId", "edit"));
        var parentSource = fields.Single(field => field.GetProperty("name").GetString() == "ParentDepartmentId")
            .GetProperty("dataSource");
        Assert.Equal("organization.department.parents", parentSource.GetProperty("key").GetString());
        Assert.Equal(["CompanyId"], parentSource.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        using var listView = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\views\DepartmentList.view.json"),
            Encoding.UTF8));
        var view = listView.RootElement.GetProperty("view");
        Assert.Equal("organization.department.list", view.GetProperty("key").GetString());
        Assert.Equal(
            ["CompanyId", "ECODE", "ENAME", "ParentDepartmentId", "IsEnabled", "SortOrder", "UpdatedAt"],
            view.GetProperty("columns").EnumerateArray().Select(column => column.GetProperty("field").GetString()));
        Assert.Equal(
            ["keyword", "companyId", "parentDepartmentId", "isEnabled"],
            view.GetProperty("query").GetProperty("fields").EnumerateArray()
                .Select(query => query.GetProperty("key").GetString()));
        Assert.Equal("create", Assert.Single(view.GetProperty("pageActions").EnumerateArray()).GetProperty("standardAction").GetString());
        var rowActions = view.GetProperty("rowActions").EnumerateArray().ToArray();
        Assert.Equal(["edit", "delete"], rowActions.Select(action => action.GetProperty("standardAction").GetString()));
        Assert.Equal("edit", StandardAction(rowActions, "edit"));
        Assert.Equal("delete", StandardAction(rowActions, "delete"));
        Assert.Equal("organization.department.detail", view.GetProperty("detailViewKey").GetString());
        Assert.False(view.TryGetProperty("export", out _));
        Assert.False(view.TryGetProperty("bulkActions", out _));
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("Department")]
    [InlineData("Position")]
    public void DirectoryDetail_EndsWithCanonicalAuditAndDoesNotRegisterFullPage(string modelName)
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "modules", "Organization", "metadata", "views", $"{modelName}Detail.view.json")));
        var view = document.RootElement.GetProperty("view");
        Assert.Equal(modelName, view.GetProperty("model").GetString());
        using var listDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "modules", "Organization", "metadata", "views", $"{modelName}List.view.json")));
        var list = listDocument.RootElement.GetProperty("view");
        var nameColumn = list.GetProperty("columns").EnumerateArray()
            .Single(column => column.GetProperty("field").GetString() == "ENAME");
        Assert.Equal("view", nameColumn.GetProperty("standardAction").GetString());
        Assert.DoesNotContain(list.GetProperty("rowActions").EnumerateArray(),
            action => action.GetProperty("standardAction").GetString() == "view");
        var sections = view.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal("audit", sections[^1].GetProperty("key").GetString());
        Assert.Equal(
            ["CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
            sections[^1].GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
        Assert.False(view.TryGetProperty("pageActions", out _));
        using var capsule = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "modules", "Organization", "newp.plugin.json")));
        Assert.DoesNotContain(view.GetProperty("key").GetString(), capsule.RootElement
            .GetProperty("frontend").GetProperty("bindings").EnumerateArray().Select(binding => binding.GetString()));
    }

    private static void AssertFileContains(string root, string relativePath, string expected)
    {
        var path = Path.Combine(root, relativePath);

        Assert.True(File.Exists(path), $"未找到元数据文件：{path}");
        Assert.Contains(expected, File.ReadAllText(path, Encoding.UTF8));
    }

    private static bool IsCreateRequired(JsonElement[] fields, string fieldName) =>
        fields.Single(field => field.GetProperty("name").GetString() == fieldName)
            .GetProperty("form")
            .GetProperty("create")
            .TryGetProperty("required", out var required)
        && required.GetBoolean();

    private static bool IsEditRequired(JsonElement[] fields, string fieldName) =>
        fields.Single(field => field.GetProperty("name").GetString() == fieldName)
            .GetProperty("form")
            .GetProperty("edit")
            .TryGetProperty("required", out var required)
        && required.GetBoolean();

    private static string? FormMode(JsonElement[] fields, string fieldName, string mode) =>
        fields.Single(field => field.GetProperty("name").GetString() == fieldName)
            .GetProperty("form")
            .GetProperty(mode)
            .GetProperty("mode")
            .GetString();

    private static string? StandardAction(JsonElement[] actions, string actionKey) =>
        actions.Single(action => action.GetProperty("key").GetString() == actionKey)
            .GetProperty("standardAction")
            .GetString();

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "tests", "Organization.Tests", "Organization.Tests.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未能定位仓库根目录。");
    }
}
