using ICP.Platform.Tools.DevSchemaSync.Database;
using ICP.Platform.Tools.DevSchemaSync.Planning;
using ICP.Platform.Tools.DevSchemaSync.Schema;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Planning;

public sealed class SchemaDiffEngineTests
{
    [Fact]
    public void BuildPlan_ReportsUnmanagedTablesWithoutDropTableOperation()
    {
        var target = new TargetSchema([]);
        var database = new[]
        {
            new DatabaseTableSchema(
                "dbo",
                "legacy_table",
                [])
        };

        var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(target, database);

        Assert.DoesNotContain(plan.Operations, static operation => operation.Kind == SchemaOperationKind.DropTable);
        Assert.Contains("dbo.legacy_table", plan.UnmanagedTables);
    }

    [Fact]
    public void BuildPlan_UsesHistoryNameToProduceRenameColumnOperation()
    {
        var target = new TargetSchema(
        [
            new TargetTableSchema(
                "ConfigCenter",
                "ConfigItem",
                "ConfigItem",
                "config_items",
                [
                    new TargetColumnSchema("DisplayName", "DisplayName", "display_name", "nvarchar", true)
                ],
                [])
        ]);

        var database = new[]
        {
            new DatabaseTableSchema(
                "dbo",
                "config_items",
                [
                    new DatabaseColumnSchema("display_name", "nvarchar", true)
                ])
        };

        var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(target, database);

        var operation = Assert.Single(plan.Operations, static item => item.Kind == SchemaOperationKind.RenameColumn);
        Assert.Equal(RiskLevel.Confirm, operation.RiskLevel);
        Assert.Equal("ConfigItem", operation.TableName);
        Assert.Equal("display_name", operation.FromName);
        Assert.Equal("DisplayName", operation.ColumnName);
    }

    [Fact]
    public void BuildPlan_DoesNotReportAlterColumnTypeForStringAndNvarchar()
    {
        var target = new TargetSchema(
        [
            new TargetTableSchema(
                "ConfigCenter",
                "ConfigItem",
                "ConfigItem",
                null,
                [
                    new TargetColumnSchema("ENAME", "ENAME", null, "string", true)
                ],
                [])
        ]);

        var database = new[]
        {
            new DatabaseTableSchema(
                "dbo",
                "ConfigItem",
                [
                    new DatabaseColumnSchema("ENAME", "nvarchar", false)
                ])
        };

        var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(target, database);

        Assert.DoesNotContain(
            plan.Operations,
            static item => item.Kind == SchemaOperationKind.AlterColumnType && item.ColumnName == "ENAME");
    }

    [Fact]
    public void BuildPlan_DoesNotReportAlterColumnTypeForDatetimeAndDatetime2()
    {
        var target = new TargetSchema(
        [
            new TargetTableSchema(
                "ConfigCenter",
                "ConfigItem",
                "ConfigItem",
                null,
                [
                    new TargetColumnSchema("CreatedAt", "CreatedAt", null, "datetime", true)
                ],
                [])
        ]);

        var database = new[]
        {
            new DatabaseTableSchema(
                "dbo",
                "ConfigItem",
                [
                    new DatabaseColumnSchema("CreatedAt", "datetime2", false)
                ])
        };

        var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(target, database);

        Assert.DoesNotContain(
            plan.Operations,
            static item => item.Kind == SchemaOperationKind.AlterColumnType && item.ColumnName == "CreatedAt");
    }
}
