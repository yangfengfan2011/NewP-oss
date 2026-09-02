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
    public void GetPermissions_DeclaresPersonAndPositionLifecyclePermissions()
    {
        var module = new OrganizationModule();
        var permissions = module.GetPermissions();

        Assert.Contains(permissions, permission => permission.Code == "Organization.Person.View");
        Assert.Contains(permissions, permission => permission.Code == "Organization.Person.Create");
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
        AssertFileContains(root, @"modules\Organization\metadata\models\Person.model.json", "\"type\": \"model\"");
        AssertFileContains(root, @"modules\Organization\metadata\dictionaries\PersonListView.dic.json", "\"type\": \"dic\"");
        AssertFileContains(root, @"modules\Organization\metadata\dictionaries\PersonDetailView.dic.json", "\"type\": \"dic\"");
        AssertFileContains(root, @"modules\Organization\metadata\dictionaries\PositionListView.dic.json", "\"type\": \"dic\"");
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

        using var listView = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, @"modules\Organization\metadata\views\PersonList.view.json"),
            Encoding.UTF8));
        var actions = listView.RootElement.GetProperty("view").GetProperty("rowActions").EnumerateArray().ToArray();
        Assert.Equal("Organization.Person.Update", RequiredPermission(actions, "edit"));
        Assert.Equal("Organization.Person.Update", RequiredPermission(actions, "disable"));
        Assert.Equal("Organization.Person.Update", RequiredPermission(actions, "enable"));

        var advancedFields = listView.RootElement.GetProperty("view")
            .GetProperty("advancedFilter")
            .GetProperty("sections")[0]
            .GetProperty("fields")
            .EnumerateArray()
            .ToArray();
        var positionFilter = advancedFields.Single(field => field.GetProperty("key").GetString() == "positionId");
        Assert.False(positionFilter.TryGetProperty("dependsOn", out _));
    }

    [Fact]
    public void PositionMetadata_UsesStandardEditDrawerWithUpdatePermission()
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
