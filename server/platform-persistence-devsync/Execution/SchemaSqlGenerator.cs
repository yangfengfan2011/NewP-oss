using System.Text;
using ICP.Platform.Persistence.DevSync.Database;
using ICP.Platform.Persistence.DevSync.Planning;
using ICP.Platform.Persistence.DevSync.Schema;

namespace ICP.Platform.Persistence.DevSync.Execution;

/// <summary>
/// 为结构同步计划生成 SQL Server 预览语句。
/// 预览既供 CLI 展示，也作为执行器输入；无法安全定位表/列时会抛出异常并阻断执行。
/// </summary>
public sealed class SchemaSqlGenerator
{
    /// <summary>
    /// 基于当前 plan、目标 schema 与数据库快照补齐 SQL 预览，保证执行器与终端预览共享同一份 SQL 来源。
    /// </summary>
    public SchemaPlan PopulateSqlPreview(
        SchemaPlan plan,
        TargetSchema targetSchema,
        IReadOnlyList<DatabaseTableSchema> databaseTables)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(targetSchema);
        ArgumentNullException.ThrowIfNull(databaseTables);

        var operations = plan.Operations
            .Select(operation => operation with
            {
                SqlPreview = GenerateSqlPreview(operation, targetSchema, databaseTables)
            })
            .ToArray();

        return plan with
        {
            Operations = operations
        };
    }

    private static IReadOnlyList<string> GenerateSqlPreview(
        SchemaOperation operation,
        TargetSchema targetSchema,
        IReadOnlyList<DatabaseTableSchema> databaseTables)
    {
        var targetTable = targetSchema.Tables.FirstOrDefault(
            table => string.Equals(table.TableName, operation.TableName, StringComparison.OrdinalIgnoreCase));
        var schemaName = ResolveSchemaName(operation, targetTable, databaseTables);

        return operation.Kind switch
        {
            SchemaOperationKind.CreateTable => [BuildCreateTableSql(targetTable ?? throw CreateMissingTableException(operation), schemaName)],
            SchemaOperationKind.AddColumn => [BuildAddColumnSql(targetTable ?? throw CreateMissingTableException(operation), operation.ColumnName, schemaName)],
            SchemaOperationKind.DropColumn => [BuildDropColumnSql(operation, schemaName)],
            SchemaOperationKind.RenameTable => [BuildRenameTableSql(operation, schemaName)],
            SchemaOperationKind.RenameColumn => [BuildRenameColumnSql(targetTable ?? throw CreateMissingTableException(operation), operation, schemaName)],
            SchemaOperationKind.AlterColumnType => [BuildAlterColumnSql(targetTable ?? throw CreateMissingTableException(operation), operation.ColumnName, schemaName)],
            SchemaOperationKind.AlterNullability => [BuildAlterColumnSql(targetTable ?? throw CreateMissingTableException(operation), operation.ColumnName, schemaName)],
            _ => throw new NotSupportedException($"当前不支持为操作 '{operation.Kind}' 生成 SQL。")
        };
    }

    private static string BuildCreateTableSql(TargetTableSchema targetTable, string schemaName)
    {
        if (targetTable.Columns.Count == 0)
        {
            throw new InvalidOperationException($"表 '{targetTable.TableName}' 没有可生成的列定义。");
        }

        var builder = new StringBuilder();
        builder.Append("CREATE TABLE ");
        builder.Append(QuoteTableName(schemaName, targetTable.TableName));
        builder.AppendLine(" (");

        for (var index = 0; index < targetTable.Columns.Count; index++)
        {
            var column = targetTable.Columns[index];
            builder.Append("    ");
            builder.Append(BuildColumnDefinition(column));

            if (index < targetTable.Columns.Count - 1)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        builder.Append(");");
        return builder.ToString();
    }

    private static string BuildAddColumnSql(TargetTableSchema targetTable, string? columnName, string schemaName)
    {
        var targetColumn = FindTargetColumn(targetTable, columnName);
        return $"ALTER TABLE {QuoteTableName(schemaName, targetTable.TableName)} ADD {BuildColumnDefinition(targetColumn)};";
    }

    private static string BuildDropColumnSql(SchemaOperation operation, string schemaName)
    {
        if (string.IsNullOrWhiteSpace(operation.ColumnName))
        {
            throw new InvalidOperationException($"操作 '{operation.Kind}' 缺少列名。");
        }

        return $"ALTER TABLE {QuoteTableName(schemaName, operation.TableName)} DROP COLUMN {QuoteIdentifier(operation.ColumnName)};";
    }

    private static string BuildRenameTableSql(SchemaOperation operation, string schemaName)
    {
        if (string.IsNullOrWhiteSpace(operation.FromName))
        {
            throw new InvalidOperationException("RenameTable 操作缺少原表名。");
        }

        return $"EXEC sp_rename N'{EscapeSqlLiteral(QuoteTableName(schemaName, operation.FromName))}', N'{EscapeSqlLiteral(operation.TableName)}';";
    }

    private static string BuildRenameColumnSql(TargetTableSchema targetTable, SchemaOperation operation, string schemaName)
    {
        if (string.IsNullOrWhiteSpace(operation.FromName))
        {
            throw new InvalidOperationException("RenameColumn 操作缺少原列名。");
        }

        var targetColumn = FindTargetColumn(targetTable, operation.ColumnName);
        return $"EXEC sp_rename N'{EscapeSqlLiteral($"{QuoteTableName(schemaName, targetTable.TableName)}.{QuoteIdentifier(operation.FromName)}")}', N'{EscapeSqlLiteral(targetColumn.StoreName)}', N'COLUMN';";
    }

    private static string BuildAlterColumnSql(TargetTableSchema targetTable, string? columnName, string schemaName)
    {
        var targetColumn = FindTargetColumn(targetTable, columnName);
        return $"ALTER TABLE {QuoteTableName(schemaName, targetTable.TableName)} ALTER COLUMN {BuildColumnDefinition(targetColumn)};";
    }

    private static string BuildColumnDefinition(TargetColumnSchema targetColumn)
    {
        return $"{QuoteIdentifier(targetColumn.StoreName)} {NormalizeStoreType(targetColumn.StoreType)} {(targetColumn.Required ? "NOT NULL" : "NULL")}";
    }

    /// <summary>
    /// 当前 target schema 中仍可能出现通用 store type，这里只做 SQL Server 最小必要映射，优先保证预览与执行可落地。
    /// </summary>
    private static string NormalizeStoreType(string storeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeType);

        var trimmed = storeType.Trim();
        var normalized = trimmed.ToLowerInvariant();

        return normalized switch
        {
            "string" => "nvarchar(200)",
            "nvarchar" => "nvarchar(200)",
            "varchar" => "varchar(200)",
            "text" => "nvarchar(max)",
            "json" => "nvarchar(max)",
            "guid" => "uniqueidentifier",
            "bool" => "bit",
            "int" => "int",
            "integer" => "int",
            "long" => "bigint",
            "bigint" => "bigint",
            "decimal" => "decimal(18,2)",
            "date" => "datetime2",
            "datetime" => "datetime2",
            "datetime2" => "datetime2",
            _ when normalized.StartsWith("nvarchar(", StringComparison.Ordinal) => trimmed,
            _ when normalized.StartsWith("varchar(", StringComparison.Ordinal) => trimmed,
            _ when normalized.StartsWith("decimal(", StringComparison.Ordinal) => trimmed,
            _ when normalized.StartsWith("numeric(", StringComparison.Ordinal) => trimmed,
            _ when normalized.Contains('(') => trimmed,
            _ => trimmed
        };
    }

    private static string ResolveSchemaName(
        SchemaOperation operation,
        TargetTableSchema? targetTable,
        IReadOnlyList<DatabaseTableSchema> databaseTables)
    {
        if (TryFindDatabaseTable(databaseTables, operation.FromName) is { } fromTable)
        {
            return fromTable.Schema;
        }

        if (TryFindDatabaseTable(databaseTables, operation.TableName) is { } currentTable)
        {
            return currentTable.Schema;
        }

        if (targetTable is not null && TryFindDatabaseTable(databaseTables, targetTable.HistoryName) is { } historyTable)
        {
            return historyTable.Schema;
        }

        return "dbo";
    }

    private static DatabaseTableSchema? TryFindDatabaseTable(
        IReadOnlyList<DatabaseTableSchema> databaseTables,
        string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return null;
        }

        var matches = databaseTables
            .Where(table => string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length == 1)
        {
            return matches[0];
        }

        return matches.FirstOrDefault(static table => string.Equals(table.Schema, "dbo", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"数据库中存在多个同名表 '{tableName}'，当前无法安全生成 SQL。");
    }

    private static TargetColumnSchema FindTargetColumn(TargetTableSchema targetTable, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new InvalidOperationException($"表 '{targetTable.TableName}' 的操作缺少列名。");
        }

        return targetTable.Columns.FirstOrDefault(
                column => string.Equals(column.StoreName, columnName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"无法在目标 schema 中找到列 '{targetTable.TableName}.{columnName}'。");
    }

    private static InvalidOperationException CreateMissingTableException(SchemaOperation operation)
    {
        return new InvalidOperationException($"无法在目标 schema 中找到表 '{operation.TableName}'。");
    }

    private static string QuoteTableName(string schemaName, string tableName)
    {
        return $"{QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)}";
    }

    private static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
