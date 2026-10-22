using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class EfModelProjectionTests
{
    [Fact]
    public void OnModelCreating_ProjectsTableAndPrimaryKey()
    {
        var definitions = new[]
        {
            new ModelDefinition(
                "ConfigItem",
                "ConfigItem",
                ModelKind.Entity,
                new[]
                {
                    new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.Text, "string", true, true, false),
                    new FieldDefinition("Key", "Key", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
                },
                new[]
                {
                    new IndexDefinition("IX_ConfigItem_Key", new[] { "Key" }, false)
                })
        };

        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(nameof(OnModelCreating_ProjectsTableAndPrimaryKey))
            .Options;

        using var db = new EfPlatformDbContext(options, definitions);
        var entityType = db.Model.FindEntityType("ConfigItem");
        var table = StoreObjectIdentifier.Table("ConfigItem", "dbo");

        Assert.NotNull(entityType);
        Assert.Equal("ConfigItem", entityType!.GetTableName());
        Assert.Equal("dbo", entityType.GetSchema());
        Assert.Equal("Id", entityType.FindProperty("Id")!.GetColumnName(table));
        Assert.Equal("Key", entityType.FindProperty("Key")!.GetColumnName(table));
        Assert.NotNull(entityType.FindPrimaryKey());
        Assert.NotNull(entityType.GetIndexes().SingleOrDefault(index => index.GetDatabaseName() == "IX_ConfigItem_Key"));
    }

    [Fact]
    public void OnModelCreating_ProjectsCalendarDateAsNullableSqlDate()
    {
        var definitions = new[]
        {
            new ModelDefinition(
                "Person",
                "Person",
                ModelKind.Entity,
                [
                    new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
                    new FieldDefinition("BirthDate", "BirthDate", FieldKind.Scalar, FieldValueType.CalendarDate, "date", false, false, false)
                ],
                [])
        };
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=NewP_ModelProjection;Integrated Security=True")
            .Options;

        using var db = new EfPlatformDbContext(options, definitions);
        var property = db.Model.FindEntityType("Person")!.FindProperty("BirthDate")!;

        Assert.Equal(typeof(DateOnly?), property.ClrType);
        Assert.Equal("date", property.GetColumnType());
    }

    [Fact]
    public void OnModelCreating_ExplicitNullableUniqueScopeRemovesSqlServerNullFilter()
    {
        var definitions = new[]
        {
            new ModelDefinition(
                "Department",
                "Department",
                ModelKind.Entity,
                [
                    new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
                    new FieldDefinition("CompanyId", "CompanyId", FieldKind.Reference, FieldValueType.ObjectId, "guid", true, false, false),
                    new FieldDefinition("ParentDepartmentId", "ParentDepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "guid", false, false, false),
                    new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
                ],
                [
                    new IndexDefinition(
                        "UX_Department_Company_Parent_ENAME",
                        ["CompanyId", "ParentDepartmentId", "ENAME"],
                        IsUnique: true,
                        IncludeNullValues: true)
                ])
        };
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=NewP_NullableUniqueProjection;Integrated Security=True")
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;

        using var db = new EfPlatformDbContext(options, definitions);
        var index = Assert.Single(db.Model.FindEntityType("Department")!.GetIndexes());

        Assert.True(index.IsUnique);
        Assert.Null(index.GetFilter());
    }
}
