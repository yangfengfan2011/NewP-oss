using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class EfRelationshipProjectionTests
{
    [Fact]
    public void ResolveModelKey_UsesDeclaredGlobalNameAndRejectsFormerQualifiedAlias()
    {
        var part = new ModelDefinition("Part", "Part", ModelKind.Entity,
            [Field("EID", FieldValueType.ObjectId, isPrimaryKey: true)], [], OwnerModuleId: "Part");
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(nameof(ResolveModelKey_UsesDeclaredGlobalNameAndRejectsFormerQualifiedAlias))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options, [part]);

        Assert.Equal("Part", db.ResolveModelKey("Part"));
        Assert.Throws<InvalidOperationException>(() => db.ResolveModelKey("Part/Part"));
        Assert.Equal("Part", Assert.Single(db.Model.GetEntityTypes()).Name);
    }

    [Fact]
    public void RuntimeModel_ProjectsLongRevisionAndRequiredUserForeignKey()
    {
        var user = new ModelDefinition(
            "IdentityAccount",
            "IdentityAccount",
            ModelKind.Entity,
            [
                Field("EID", FieldValueType.ObjectId, isPrimaryKey: true),
                Field("Revision", FieldValueType.Integer)
            ],
            [],
            OwnerModuleId: "Identity");
        var credential = new ModelDefinition(
            "LocalCredential",
            "LocalCredential",
            ModelKind.Entity,
            [
                Field("EID", FieldValueType.ObjectId, isPrimaryKey: true),
                Field("AccountEID", FieldValueType.ObjectId),
                new FieldDefinition(
                    "RevokedAt",
                    "RevokedAt",
                    FieldKind.Scalar,
                    FieldValueType.DateTime,
                    "datetime",
                    Required: false,
                    IsPrimaryKey: false,
                    IsAuditField: false)
            ],
            [new IndexDefinition("UX_LocalCredential_AccountEID", ["AccountEID"], true)],
            OwnerModuleId: "Identity",
            ForeignKeys:
            [
                new ForeignKeyDefinition(
                    "FK_LocalCredential_IdentityAccount",
                    ["AccountEID"],
                    "IdentityAccount",
                    ["EID"],
                    ForeignKeyDeleteBehavior.Restrict)
            ]);
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(nameof(RuntimeModel_ProjectsLongRevisionAndRequiredUserForeignKey))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;

        using var dbContext = new EfPlatformDbContext(options, [credential, user]);

        var userType = dbContext.Model.FindEntityType("IdentityAccount");
        var credentialType = dbContext.Model.FindEntityType("LocalCredential");
        Assert.Equal(typeof(long), userType!.FindProperty("Revision")!.ClrType);
        Assert.Equal(typeof(DateTime?), credentialType!.FindProperty("RevokedAt")!.ClrType);
        var foreignKey = Assert.Single(credentialType!.GetForeignKeys());
        Assert.Equal("IdentityAccount", foreignKey.PrincipalEntityType.Name);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(credentialType.FindIndex(credentialType.FindProperty("AccountEID")!)!.IsUnique);
    }

    private static FieldDefinition Field(
        string name,
        FieldValueType valueType,
        bool isPrimaryKey = false) =>
        new(
            name,
            name,
            FieldKind.Scalar,
            valueType,
            valueType == FieldValueType.Integer ? "bigint" : "nvarchar",
            Required: true,
            IsPrimaryKey: isPrimaryKey,
            IsAuditField: false);
}
