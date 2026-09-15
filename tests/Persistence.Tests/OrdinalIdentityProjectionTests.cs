using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
namespace ICP.Platform.Persistence.Tests;
public sealed class OrdinalIdentityProjectionTests
{
    [Fact]
    public void FormalFoundationChainValidatesBeforeOpeningAnyConnection()
    {
        var plan = TestPersistencePlan.Create();
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().UseSqlServer("Server=localhost;Database=never-connect;Integrated Security=true;TrustServerCertificate=true").Options, plan);
        Assert.NotEmpty(new ModuleMigrationExecutor(db, plan).ValidateBeforeConnection());
    }
    [Fact]
    public void FullOrdinalKeyUsesComputedPhysicalIndexWithoutTruncatingIdentity()
    {
        ModelDefinition model = new("KeyProbe", "KeyProbe", ModelKind.Entity,
            [new("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "nvarchar", true, true, false),
             new("Key", "Key", FieldKind.Scalar, FieldValueType.Text, "nvarchar", true, false, false) { OrdinalKey = true }],
            [new("UX_KeyProbe_Key", ["Key"], true)]);
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().UseSqlServer("Server=localhost;Database=never-connect;Integrated Security=true;TrustServerCertificate=true").Options, [model]);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(model.LogicalKey)!;
        Assert.Equal("nvarchar(max)", entity.FindProperty("Key")!.GetColumnType());
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty("Key")!.GetCollation());
        var hash = entity.FindProperty("__ordinal_Key")!;
        Assert.Equal("isnull(CONVERT([nvarchar](64),hashbytes('SHA2_256',CONVERT([varbinary](max),[Key])),(2)),N'')", hash.GetComputedColumnSql());
        Assert.True(hash.GetIsStored());
        Assert.Equal(64, hash.GetMaxLength());
        Assert.Equal("__ordinal_Key", Assert.Single(Assert.Single(entity.GetIndexes()).Properties).Name);
    }
}
