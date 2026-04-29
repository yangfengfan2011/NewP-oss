using ICP.Platform.Persistence.DevSync.Database;
using ICP.Platform.Persistence.DevSync.Schema;

namespace ICP.Platform.Persistence.DevSync.Planning;

/// <summary>
/// 对比目标 schema 与数据库快照并生成结构同步计划。
/// 该内核只做显式 diff 和冲突阻断，不依赖复制顺序、隐式覆盖或运行时视图作为 authoring 真源。
/// </summary>
public sealed class SchemaDiffEngine
{
    private readonly RiskClassifier _riskClassifier;

    /// <summary>
    /// 创建结构 diff 内核。
    /// </summary>
    /// <param name="riskClassifier">结构操作风险分类器。</param>
    public SchemaDiffEngine(RiskClassifier riskClassifier)
    {
        _riskClassifier = riskClassifier ?? throw new ArgumentNullException(nameof(riskClassifier));
    }

    /// <summary>
    /// 对比目标 schema 与数据库快照，生成只读计划；数据库多出的表只归为未托管对象，不自动删表。
    /// </summary>
    public SchemaPlan BuildPlan(TargetSchema targetSchema, IReadOnlyList<DatabaseTableSchema> databaseTables)
    {
        ArgumentNullException.ThrowIfNull(targetSchema);
        ArgumentNullException.ThrowIfNull(databaseTables);

        var databaseByName = BuildDatabaseTableLookup(databaseTables);
        var matchedDatabaseTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operations = new List<SchemaOperation>();
        var managedObjects = new List<string>();

        foreach (var targetTable in targetSchema.Tables.OrderBy(static item => item.TableName, StringComparer.OrdinalIgnoreCase))
        {
            managedObjects.Add(targetTable.TableName);

            var currentDatabaseTable = TryGetUniqueTable(databaseByName, targetTable.TableName);
            var historyDatabaseTable = string.IsNullOrWhiteSpace(targetTable.HistoryName)
                ? null
                : TryGetUniqueTable(databaseByName, targetTable.HistoryName!);

            if (currentDatabaseTable is not null && historyDatabaseTable is not null)
            {
                throw new InvalidOperationException($"表 '{targetTable.TableName}' 的当前名称与 historyName 同时存在于数据库中。");
            }

            if (currentDatabaseTable is null && historyDatabaseTable is null)
            {
                operations.Add(CreateOperation(
                    SchemaOperationKind.CreateTable,
                    targetTable.TableName,
                    null,
                    null,
                    $"创建表 {targetTable.TableName}。"));
                continue;
            }

            var effectiveDatabaseTable = currentDatabaseTable ?? historyDatabaseTable!;
            matchedDatabaseTables.Add(BuildDatabaseTableIdentity(effectiveDatabaseTable));

            if (currentDatabaseTable is null && historyDatabaseTable is not null)
            {
                operations.Add(CreateOperation(
                    SchemaOperationKind.RenameTable,
                    targetTable.TableName,
                    null,
                    historyDatabaseTable.Name,
                    $"将表 {historyDatabaseTable.Name} 重命名为 {targetTable.TableName}。"));
            }

            operations.AddRange(BuildColumnOperations(targetTable, effectiveDatabaseTable));
        }

        var unmanagedTables = databaseTables
            .Where(table => !matchedDatabaseTables.Contains(BuildDatabaseTableIdentity(table)))
            .OrderBy(static table => table.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static table => table.Name, StringComparer.OrdinalIgnoreCase)
            .Select(BuildDatabaseTableIdentity)
            .ToArray();

        var summary = new SchemaPlanSummary(
            ManagedTableCount: managedObjects.Count,
            OperationCount: operations.Count,
            SafeOperationCount: operations.Count(operation => string.Equals(operation.RiskLevel, RiskLevel.Safe, StringComparison.Ordinal)),
            ConfirmOperationCount: operations.Count(operation => string.Equals(operation.RiskLevel, RiskLevel.Confirm, StringComparison.Ordinal)),
            UnmanagedTableCount: unmanagedTables.Length);

        return new SchemaPlan(summary, managedObjects, operations, unmanagedTables);
    }

    private IEnumerable<SchemaOperation> BuildColumnOperations(TargetTableSchema targetTable, DatabaseTableSchema databaseTable)
    {
        var operations = new List<SchemaOperation>();
        var databaseColumns = databaseTable.Columns.ToDictionary(static column => column.Name, StringComparer.OrdinalIgnoreCase);
        var matchedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var targetColumn in targetTable.Columns.OrderBy(static item => item.StoreName, StringComparer.OrdinalIgnoreCase))
        {
            databaseColumns.TryGetValue(targetColumn.StoreName, out var currentColumn);

            DatabaseColumnSchema? historyColumn = null;
            if (!string.IsNullOrWhiteSpace(targetColumn.HistoryName))
            {
                databaseColumns.TryGetValue(targetColumn.HistoryName!, out historyColumn);
            }

            if (currentColumn is not null && historyColumn is not null && !string.Equals(currentColumn.Name, historyColumn.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"列 '{targetTable.TableName}.{targetColumn.StoreName}' 的当前名称与 historyName 同时存在于数据库中。");
            }

            if (currentColumn is null && historyColumn is null)
            {
                operations.Add(CreateOperation(
                    SchemaOperationKind.AddColumn,
                    targetTable.TableName,
                    targetColumn.StoreName,
                    null,
                    $"向表 {targetTable.TableName} 添加列 {targetColumn.StoreName}。"));
                continue;
            }

            var effectiveColumn = currentColumn ?? historyColumn!;
            matchedColumns.Add(effectiveColumn.Name);

            if (currentColumn is null && historyColumn is not null)
            {
                operations.Add(CreateOperation(
                    SchemaOperationKind.RenameColumn,
                    targetTable.TableName,
                    targetColumn.StoreName,
                    historyColumn.Name,
                    $"将列 {targetTable.TableName}.{historyColumn.Name} 重命名为 {targetColumn.StoreName}。"));
            }

            if (!AreEquivalentStoreTypes(effectiveColumn.StoreType, targetColumn.StoreType))
            {
                operations.Add(CreateOperation(
                    SchemaOperationKind.AlterColumnType,
                    targetTable.TableName,
                    targetColumn.StoreName,
                    effectiveColumn.StoreType,
                    $"将列 {targetTable.TableName}.{targetColumn.StoreName} 的类型从 {effectiveColumn.StoreType} 调整为 {targetColumn.StoreType}。"));
            }

            var expectedIsNullable = !targetColumn.Required;
            if (effectiveColumn.IsNullable != expectedIsNullable)
            {
                operations.Add(CreateOperation(
                    SchemaOperationKind.AlterNullability,
                    targetTable.TableName,
                    targetColumn.StoreName,
                    effectiveColumn.IsNullable ? "NULL" : "NOT NULL",
                    $"将列 {targetTable.TableName}.{targetColumn.StoreName} 的可空性调整为 {(expectedIsNullable ? "NULL" : "NOT NULL")}。"));
            }
        }

        foreach (var databaseColumn in databaseTable.Columns
                     .Where(column => !matchedColumns.Contains(column.Name))
                     .OrderBy(static column => column.Name, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(CreateOperation(
                SchemaOperationKind.DropColumn,
                targetTable.TableName,
                databaseColumn.Name,
                null,
                $"删除表 {targetTable.TableName} 上未受管的列 {databaseColumn.Name}。"));
        }

        return operations;
    }

    /// <summary>
    /// 目标 schema 和数据库快照当前来自不同投影链路，这里只做最小必要的等价映射，避免把同义类型误报成变更。
    /// </summary>
    private static bool AreEquivalentStoreTypes(string databaseStoreType, string targetStoreType)
    {
        return string.Equals(
            NormalizeStoreType(databaseStoreType),
            NormalizeStoreType(targetStoreType),
            StringComparison.Ordinal);
    }

    // 这里故意只收口当前 Task 6 已确认的等价关系，避免在缺少设计的情况下扩大类型折叠范围。
    private static string NormalizeStoreType(string storeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeType);

        var normalized = storeType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "string" => "nvarchar",
            "datetime2" => "datetime",
            _ => normalized
        };
    }

    private SchemaOperation CreateOperation(
        string kind,
        string tableName,
        string? columnName,
        string? fromName,
        string description)
    {
        return new SchemaOperation(
            kind,
            _riskClassifier.Classify(kind),
            tableName,
            columnName,
            fromName,
            description,
            []);
    }

    private static Dictionary<string, List<DatabaseTableSchema>> BuildDatabaseTableLookup(IReadOnlyList<DatabaseTableSchema> databaseTables)
    {
        var lookup = new Dictionary<string, List<DatabaseTableSchema>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in databaseTables)
        {
            if (!lookup.TryGetValue(table.Name, out var tables))
            {
                tables = [];
                lookup.Add(table.Name, tables);
            }

            tables.Add(table);
        }

        return lookup;
    }

    private static DatabaseTableSchema? TryGetUniqueTable(
        IReadOnlyDictionary<string, List<DatabaseTableSchema>> databaseByName,
        string tableName)
    {
        if (!databaseByName.TryGetValue(tableName, out var candidates))
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var dboTable = candidates.FirstOrDefault(static item => string.Equals(item.Schema, "dbo", StringComparison.OrdinalIgnoreCase));
        if (dboTable is not null)
        {
            return dboTable;
        }

        throw new InvalidOperationException($"数据库中存在多个同名表 '{tableName}'，当前工具无法安全判定目标对象。");
    }

    private static string BuildDatabaseTableIdentity(DatabaseTableSchema table)
    {
        return $"{table.Schema}.{table.Name}";
    }
}
