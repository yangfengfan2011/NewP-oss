using ICP.Platform.Persistence.DevSync.Database;
using ICP.Platform.Persistence.DevSync.Execution;
using ICP.Platform.Persistence.DevSync.Planning;
using ICP.Platform.Persistence.DevSync.Schema;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Execution;

public sealed class SchemaSqlGeneratorTests
{
    [Fact]
    public void PopulateSqlPreview_ForCreateTable_IncludesUniqueAndNormalIndexes()
    {
        var plan = CreatePlan(
            new SchemaOperation(
                SchemaOperationKind.CreateTable,
                RiskLevel.Safe,
                "ConfigItem",
                null,
                null,
                "创建表 ConfigItem。",
                []));
        var targetSchema = new TargetSchema(
        [
            new TargetTableSchema(
                "ConfigCenter",
                "ConfigItem",
                "ConfigItem",
                null,
                [
                    new TargetColumnSchema("EID", "EID", null, "nvarchar(128)", true),
                    new TargetColumnSchema("ModuleId", "ModuleId", null, "nvarchar(256)", true)
                ],
                [
                    new TargetIndexSchema("UX_ConfigItem_EID", ["EID"], true),
                    new TargetIndexSchema("IX_ConfigItem_Module", ["ModuleId"], false)
                ])
        ]);

        var previewPlan = new SchemaSqlGenerator().PopulateSqlPreview(plan, targetSchema, []);

        var operation = Assert.Single(previewPlan.Operations);
        Assert.Contains(operation.SqlPreview, static sql =>
            sql.Contains("CREATE TABLE [dbo].[ConfigItem]", StringComparison.Ordinal));
        Assert.Contains(operation.SqlPreview, static sql =>
            sql.Contains("CREATE UNIQUE INDEX [UX_ConfigItem_EID] ON [dbo].[ConfigItem] ([EID]);", StringComparison.Ordinal));
        Assert.Contains(operation.SqlPreview, static sql =>
            sql.Contains("CREATE INDEX [IX_ConfigItem_Module] ON [dbo].[ConfigItem] ([ModuleId]);", StringComparison.Ordinal));
    }

    [Fact]
    public void PopulateSqlPreview_ForDateTimeColumn_UsesDatetime2()
    {
        var plan = CreatePlan(
            new SchemaOperation(
                SchemaOperationKind.AddColumn,
                RiskLevel.Safe,
                "ConfigItem",
                "PublishedAt",
                null,
                "向表 ConfigItem 添加列 PublishedAt。",
                []));
        var targetSchema = new TargetSchema(
        [
            new TargetTableSchema(
                "ConfigCenter",
                "ConfigItem",
                "ConfigItem",
                null,
                [
                    new TargetColumnSchema("PublishedAt", "PublishedAt", null, "datetime", true)
                ],
                [])
        ]);
        var databaseTables = new[]
        {
            new DatabaseTableSchema(
                "dbo",
                "ConfigItem",
                [])
        };

        var previewPlan = new SchemaSqlGenerator().PopulateSqlPreview(plan, targetSchema, databaseTables);

        var operation = Assert.Single(previewPlan.Operations);
        var sql = Assert.Single(operation.SqlPreview);
        Assert.Contains("ALTER TABLE [dbo].[ConfigItem] ADD [PublishedAt] datetime2 NOT NULL;", sql, StringComparison.Ordinal);
    }

    private static SchemaPlan CreatePlan(params SchemaOperation[] operations)
    {
        var confirmOperationCount = operations.Count(operation => string.Equals(operation.RiskLevel, RiskLevel.Confirm, StringComparison.Ordinal));
        return new SchemaPlan(
            new SchemaPlanSummary(
                ManagedTableCount: 1,
                OperationCount: operations.Length,
                SafeOperationCount: operations.Length - confirmOperationCount,
                ConfirmOperationCount: confirmOperationCount,
                UnmanagedTableCount: 0),
            ["dbo.ConfigItem"],
            operations,
            []);
    }
}
