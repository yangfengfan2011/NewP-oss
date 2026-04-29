using Microsoft.Data.SqlClient;

namespace ICP.Platform.Persistence.DevSync.Database;

/// <summary>
/// 读取 SQL Server 当前结构快照的边界适配器。
/// 该类型只负责把系统目录投影为 <see cref="DatabaseTableSchema"/>，连接失败、权限不足或目录读取异常会向上抛出作为启动阻断诊断。
/// </summary>
public sealed class SqlServerSchemaReader
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ColumnRow>>> columnLoader;

    /// <summary>
    /// 创建使用真实 SQL Server 连接的 schema reader。
    /// </summary>
    public SqlServerSchemaReader()
        : this(LoadColumnRowsAsync)
    {
    }

    internal SqlServerSchemaReader(Func<string, CancellationToken, Task<IReadOnlyList<ColumnRow>>> columnLoader)
    {
        this.columnLoader = columnLoader;
    }

    /// <summary>
    /// 读取用户表和列的当前数据库快照；调用方应把异常视为环境或权限失败信号，而不是空库。
    /// </summary>
    /// <param name="connectionString">SQL Server 连接字符串。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <returns>按 schema/table 排序后的数据库结构快照。</returns>
    public async Task<DatabaseSchema> ReadAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var rows = await columnLoader(connectionString, cancellationToken);
        var tables = rows
            .GroupBy(
                static row => new TableKey(row.SchemaName, row.TableName))
            .OrderBy(static group => group.Key.SchemaName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static group => group.Key.TableName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DatabaseTableSchema(
                group.Key.SchemaName,
                group.Key.TableName,
                group
                    .Select(static row => new DatabaseColumnSchema(
                        row.ColumnName,
                        row.StoreType,
                        row.IsNullable))
                    .ToArray()))
            .ToArray();

        return new DatabaseSchema(tables);
    }

    internal sealed record ColumnRow(
        string SchemaName,
        string TableName,
        string ColumnName,
        string StoreType,
        bool IsNullable);

    internal sealed record TableKey(
        string SchemaName,
        string TableName);

    /// <summary>
    /// SQL Server 当前结构快照；只表示读取时刻的结果，不作为长期 authoring 真源。
    /// </summary>
    /// <param name="Tables">数据库中的用户表集合。</param>
    public sealed record DatabaseSchema(IReadOnlyList<DatabaseTableSchema> Tables);

    private static async Task<IReadOnlyList<ColumnRow>> LoadColumnRowsAsync(string connectionString, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.name AS SchemaName,
                t.name AS TableName,
                c.name AS ColumnName,
                ty.name AS StoreType,
                c.is_nullable AS IsNullable
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            INNER JOIN sys.columns AS c ON c.object_id = t.object_id
            INNER JOIN sys.types AS ty
                ON ty.user_type_id = c.user_type_id
                AND ty.system_type_id = c.system_type_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id;
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<ColumnRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ColumnRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4)));
        }

        return rows;
    }
}
