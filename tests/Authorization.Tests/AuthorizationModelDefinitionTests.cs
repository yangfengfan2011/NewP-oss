using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class AuthorizationModelDefinitionTests
{
    [Fact]
    public void Definitions_ContainExpectedEntityAndLinkModels()
    {
        var definitions = AuthorizationModelDefinitions.All;

        Assert.Contains(definitions, static definition => definition.Name == "Role" && definition.Kind == ModelKind.Entity);
        Assert.Contains(definitions, static definition => definition.Name == "Permission" && definition.Kind == ModelKind.Entity);
        Assert.Contains(definitions, static definition => definition.Name == "RolePermission" && definition.Kind == ModelKind.Link);
        Assert.Contains(definitions, static definition => definition.Name == "UserRole" && definition.Kind == ModelKind.Link);

        var validator = new ModelDefinitionValidator();
        foreach (var definition in definitions)
        {
            validator.Validate(definition);
        }
    }

    [Fact]
    public void Authorization_DefinitionsUsePascalCaseStoreNames()
    {
        var role = Assert.Single(AuthorizationModelDefinitions.All, item => item.Name == "Role");

        Assert.Equal("Role", role.TableName);
        Assert.Contains(role.Fields, static field => field.Name == "Id" && field.StoreName == "Id");
        Assert.Contains(role.Fields, static field => field.Name == "Code" && field.StoreName == "Code");
        Assert.Contains(role.Fields, static field => field.Name == "Name" && field.StoreName == "Name");
    }
}
