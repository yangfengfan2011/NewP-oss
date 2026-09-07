using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationModuleTests
{
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
    public void GetPermissions_DeclaresPersonCompanyDepartmentAndPositionLifecyclePermissions()
    {
        var module = new OrganizationModule();
        var permissions = module.GetPermissions();

        Assert.Contains(permissions, permission => permission.Code == "Organization.Person.View");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Person.Create");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Company.View");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Company.Create");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Company.Update");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Company.Delete");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Department.View");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Department.Create");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Department.Update");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Department.Delete");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Position.View");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Position.Create");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Position.Update");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Position.Delete");
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
        Assert.Equal("Organization.Person.Update", RequiredPermission(actions, "edit"));
        Assert.Equal("Organization.Person.Update", RequiredPermission(actions, "disable"));
        Assert.Equal("Organization.Person.Update", RequiredPermission(actions, "enable"));

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
        Assert.Equal("Organization.Position.Update", RequiredPermission(actions, "edit"));
        var delete = actions.Single(action => action.GetProperty("key").GetString() == "delete");
        Assert.Equal("delete", delete.GetProperty("standardAction").GetString());
        Assert.Equal("Action_Delete_Confirm", delete.GetProperty("confirmMessageKey").GetString());
        Assert.Equal("Organization.Position.Delete", RequiredPermission(actions, "delete"));
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
        Assert.Equal("Organization.Company.Update", RequiredPermission(rowActions, "edit"));
        Assert.Equal("Organization.Company.Delete", RequiredPermission(rowActions, "delete"));
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
        Assert.Equal("Organization.Department.Update", RequiredPermission(rowActions, "edit"));
        Assert.Equal("Organization.Department.Delete", RequiredPermission(rowActions, "delete"));
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

    private static string? RequiredPermission(JsonElement[] actions, string actionKey) =>
        actions.Single(action => action.GetProperty("key").GetString() == actionKey)
            .GetProperty("permissions")[0]
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
