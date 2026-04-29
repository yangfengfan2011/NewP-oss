using ICP.Platform.Persistence.DevSync.Database;
using ICP.Platform.Persistence.DevSync.Execution;
using ICP.Platform.Persistence.DevSync.Planning;
using ICP.Platform.Persistence.DevSync.Schema;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Execution;

public sealed class SchemaApplyExecutorTests
{
    [Fact]
    public async Task ApplyAsync_DoesNotExecuteWhenRiskConfirmationRejected()
    {
        var executedSql = new List<string>();
        var plan = CreatePlan(
            new SchemaOperation(
                SchemaOperationKind.DropColumn,
                RiskLevel.Confirm,
                "ConfigItem",
                "Description",
                null,
                "删除表 ConfigItem 上未受管的列 Description。",
                ["ALTER TABLE [dbo].[ConfigItem] DROP COLUMN [Description];"]));

        var executor = new SchemaApplyExecutor(
            confirmAll: static _ => true,
            confirmRisk: static _ => false,
            executeSqlAsync: (sqlBatch, _) =>
            {
                executedSql.AddRange(sqlBatch);
                return Task.FromResult(sqlBatch.Count);
            });

        var result = await executor.ApplyAsync(plan);

        Assert.False(result.Applied);
        Assert.True(result.Cancelled);
        Assert.Equal(0, result.ExecutedOperationCount);
        Assert.Equal(0, result.ExecutedStatementCount);
        Assert.Empty(executedSql);
    }

    [Fact]
    public void PopulateSqlPreview_MapsGenericStringTypeToExecutableSqlServerType()
    {
        var plan = CreatePlan(
            new SchemaOperation(
                SchemaOperationKind.AddColumn,
                RiskLevel.Safe,
                "ConfigItem",
                "DisplayName",
                null,
                "向表 ConfigItem 添加列 DisplayName。",
                []));
        var targetSchema = new TargetSchema(
        [
            new TargetTableSchema(
                "ConfigCenter",
                "ConfigItem",
                "ConfigItem",
                null,
                [
                    new TargetColumnSchema("DisplayName", "DisplayName", null, "string", true)
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
        Assert.Contains("ALTER TABLE [dbo].[ConfigItem] ADD [DisplayName] nvarchar(200) NOT NULL;", sql, StringComparison.Ordinal);
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
