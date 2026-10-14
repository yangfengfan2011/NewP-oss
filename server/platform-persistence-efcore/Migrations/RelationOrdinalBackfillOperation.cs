using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ICP.Platform.Persistence.EfCore.Migrations;

/// <summary>同 owner 的 Link 到 Entity 按父分组、目标名称与关系主键排名的一次性 NULL 回填。</summary>
public sealed class RelationOrdinalBackfillOperation : MigrationOperation
{
    public required ProviderRelationOrdinalBackfillFact Fact { get; init; }
}

/// <summary>只声明固定关系排名形状，不接受 SQL、表达式或回调。</summary>
public static class RelationOrdinalMigrationBuilderExtensions
{
    /// <summary>声明只为指定真实子类型的 NULL 次序回填，从目标名称和稳定关系主键确定旧树顺序。</summary>
    public static void BackfillRelationOrdinal(this MigrationBuilder builder, string ownerModuleId, string table, string column,
        string parentColumn, string linkIdColumn, string childColumn, string typeColumn, string childModelKey,
        string targetTable, string targetIdColumn, string targetNameColumn, string schema = "dbo")
    {
        ArgumentNullException.ThrowIfNull(builder);
        var fact = new ProviderRelationOrdinalBackfillFact(ownerModuleId, schema, table, column, parentColumn, linkIdColumn,
            childColumn, typeColumn, childModelKey, targetTable, targetIdColumn, targetNameColumn);
        if (new[] { ownerModuleId, schema, table, column, parentColumn, linkIdColumn, childColumn, typeColumn,
            childModelKey, targetTable, targetIdColumn, targetNameColumn }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Relation ordinal backfill requires explicit owner, tables and columns.");
        builder.Operations.Add(new RelationOrdinalBackfillOperation { Fact = fact });
    }
}
