using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class AuthorizationModelMetadataProviderTests
{
    [Fact]
    public void ManagedRelationshipModels_AreEntitiesWithoutLinkedTypes()
    {
        var repositoryRoot = GetRepositoryRoot();

        AssertManagedEntityMetadata(
            Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "models", "RolePermission.model.json"),
            "RolePermission",
            "RoleId",
            "PermissionId",
            "UX_RolePermission_Role_Permission");

        AssertManagedEntityMetadata(
            Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "models", "UserRole.model.json"),
            "UserRole",
            "UserId",
            "RoleId",
            "UX_UserRole_User_Role",
            "IX_UserRole_Role");
    }

    [Fact]
    public void GetDefinitions_ReturnsAuthorizationModelsAndPassesDefinitionValidation()
    {
        var repositoryRoot = GetRepositoryRoot();
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "models", "Role.model.json")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "models", "Permission.model.json")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "models", "RolePermission.model.json")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "models", "UserRole.model.json")));

        var provider = new AuthorizationModelMetadataProvider();
        var validator = new ModelDefinitionValidator();

        var definitions = provider.GetDefinitions();

        Assert.Collection(
            definitions,
            role =>
            {
                Assert.Equal("Role", role.Name);
                Assert.Contains(role.Fields, static field => field.Name == "EID" && field.IsPrimaryKey);
                Assert.DoesNotContain(role.Fields, static field => field.Name == "Id");
                Assert.Contains(role.Fields, static field => field.Name == "ECODE");
                Assert.Contains(role.Fields, static field => field.Name == "ENAME");
                Assert.DoesNotContain(role.Fields, static field => field.Name == "Code");
                Assert.DoesNotContain(role.Fields, static field => field.Name == "Name");
                Assert.Contains(role.Indexes, static index => index.Name == "UX_Role_ECODE");
                validator.Validate(role);
            },
            permission =>
            {
                Assert.Equal("Permission", permission.Name);
                Assert.Contains(permission.Fields, static field => field.Name == "EID" && field.IsPrimaryKey);
                Assert.DoesNotContain(permission.Fields, static field => field.Name == "Id");
                Assert.Contains(permission.Fields, static field => field.Name == "ECODE");
                Assert.Contains(permission.Fields, static field => field.Name == "ENAME");
                Assert.DoesNotContain(permission.Fields, static field => field.Name == "Code");
                Assert.DoesNotContain(permission.Fields, static field => field.Name == "DisplayName");
                Assert.Contains(permission.Fields, static field => field.Name == "ModuleId");
                Assert.Contains(permission.Indexes, static index => index.Name == "UX_Permission_ECODE");
                Assert.Contains(permission.Indexes, static index => index.Name == "IX_Permission_Module");
                validator.Validate(permission);
            },
            rolePermission =>
            {
                Assert.Equal("RolePermission", rolePermission.Name);
                Assert.Contains(rolePermission.Fields, static field => field.Name == "RoleId");
                Assert.Contains(rolePermission.Fields, static field => field.Name == "PermissionId");
                Assert.Contains(rolePermission.Indexes, static index => index.Name == "UX_RolePermission_Role_Permission");
                validator.Validate(rolePermission);
            },
            userRole =>
            {
                Assert.Equal("UserRole", userRole.Name);
                Assert.Contains(userRole.Fields, static field => field.Name == "UserId");
                Assert.Contains(userRole.Fields, static field => field.Name == "RoleId");
                Assert.Contains(userRole.Indexes, static index => index.Name == "UX_UserRole_User_Role");
                Assert.Contains(userRole.Indexes, static index => index.Name == "IX_UserRole_Role");
                validator.Validate(userRole);
            });
    }

    private static void AssertManagedEntityMetadata(
        string modelPath,
        string modelName,
        string firstFieldName,
        string secondFieldName,
        params string[] indexNames)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(modelPath));

        var model = document.RootElement.GetProperty(modelName);
        Assert.Equal("Entity", model.GetProperty("kind").GetString());
        Assert.False(model.TryGetProperty("linkedTypes", out _));

        var fields = model.GetProperty("fields").EnumerateArray().Select(static field => field.GetProperty("name").GetString()).ToArray();
        Assert.Contains(firstFieldName, fields);
        Assert.Contains(secondFieldName, fields);

        var indexes = model.GetProperty("indexes").EnumerateArray().Select(static index => index.GetProperty("name").GetString()).ToArray();
        foreach (var indexName in indexNames)
        {
            Assert.Contains(indexName, indexes);
        }
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
