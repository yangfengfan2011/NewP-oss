using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
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
                "config_items",
                ModelKind.Entity,
                new[]
                {
                    new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.Text, "string", true, true, false),
                    new FieldDefinition("Key", "config_key", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
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

        Assert.NotNull(entityType);
        Assert.Equal("config_items", entityType!.GetTableName());
        Assert.NotNull(entityType.FindPrimaryKey());
        Assert.NotNull(entityType.GetIndexes().SingleOrDefault(index => index.GetDatabaseName() == "IX_ConfigItem_Key"));
    }
}
