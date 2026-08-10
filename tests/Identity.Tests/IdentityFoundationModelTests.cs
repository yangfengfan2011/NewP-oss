using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Identity;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Sdk;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentityFoundationModelTests
{
    [Fact]
    public void Module_UsesRequiredIdentityCapsule()
    {
        var module = new IdentityModule();

        Assert.Equal("Identity", module.ModuleId);
        Assert.Equal("identity.backend", Assert.IsAssignableFrom<IModuleBehaviorBinding>(module).BehaviorKey);
    }

    [Fact]
    public void Metadata_DefinesIdentityAggregateWithNonNullAuditAndRelationships()
    {
        var definitions = new IdentityModelMetadataProvider().GetDefinitions();

        Assert.Equal(["User", "LocalCredential", "IdentitySecurityEvent"], definitions.Select(static model => model.Name));
        var user = Assert.Single(definitions, static model => model.Name == "User");
        AssertAuditFields(user);
        Assert.Contains(user.Fields, static field => field is { Name: "ECODE", Required: true });
        Assert.Contains(user.Fields, static field => field is { Name: "ENAME", Required: true });
        Assert.Contains(user.Fields, static field => field is { Name: "NormalizedECODE", Required: true });
        Assert.Contains(user.Fields, static field => field is { Name: "Status", ValueType: FieldValueType.Enum, Required: true });
        Assert.Contains(user.Fields, static field => field is { Name: "Revision", ValueType: FieldValueType.Integer, Required: true });
        Assert.Contains(user.Indexes, static index => index is { Name: "UX_User_NormalizedECODE", IsUnique: true });

        var credential = Assert.Single(definitions, static model => model.Name == "LocalCredential");
        AssertAuditFields(credential);
        Assert.Contains(credential.Indexes, static index => index is { Name: "UX_LocalCredential_UserEID", IsUnique: true });
        Assert.Contains(credential.ForeignKeys, static foreignKey =>
            foreignKey is { Name: "FK_LocalCredential_User", PrincipalModelKey: "Identity/User" });

        var securityEvent = Assert.Single(definitions, static model => model.Name == "IdentitySecurityEvent");
        AssertAuditFields(securityEvent);
        Assert.Contains(securityEvent.ForeignKeys, static foreignKey =>
            foreignKey is { Name: "FK_IdentitySecurityEvent_TargetUser", PrincipalModelKey: "Identity/User" });
    }

    private static void AssertAuditFields(ModelDefinition definition)
    {
        Assert.Contains(definition.Fields, static field => field is { Name: "CreatedBy", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "CreatedAt", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "UpdatedBy", Required: true, IsAuditField: true });
        Assert.Contains(definition.Fields, static field => field is { Name: "UpdatedAt", Required: true, IsAuditField: true });
    }
}
