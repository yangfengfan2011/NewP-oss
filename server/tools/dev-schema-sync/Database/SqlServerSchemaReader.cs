using Microsoft.Data.SqlClient;

namespace ICP.Platform.Tools.DevSchemaSync.Database;

public sealed class SqlServerSchemaReader
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ColumnRow>>> columnLoader;

    public SqlServerSchemaReader()
        : this(LoadColumnRowsAsync)
    {
    }

    internal SqlServerSchemaReader(Func<string, CancellationToken, Task<IReadOnlyList<ColumnRow>>> columnLoader)
    {
        this.columnLoader = columnLoader;
    }

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
