using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore.ProviderTargets;

/// <summary>
/// 读取 SQL Server 系统目录并与 canonical Provider Target 比较；该边界只发出 SELECT。
/// </summary>
internal sealed class SqlServerProviderTargetConsistencyChecker
{
    private readonly IProviderTargetSource _targetSource;
    private readonly Func<CancellationToken, Task<SqlServerSchemaSnapshot>> _readSchemaAsync;

    public SqlServerProviderTargetConsistencyChecker(
        EfPlatformDbContext dbContext,
        IProviderTargetSource targetSource)
        : this(
            targetSource,
            cancellationToken => SqlServerSchemaSnapshotReader.ReadAsync(
                dbContext.Database.GetDbConnection(),
                cancellationToken))
    {
    }

    internal SqlServerProviderTargetConsistencyChecker(
        IProviderTargetSource targetSource,
        Func<CancellationToken, Task<SqlServerSchemaSnapshot>> readSchemaAsync)
    {
        _targetSource = targetSource ?? throw new ArgumentNullException(nameof(targetSource));
        _readSchemaAsync = readSchemaAsync ?? throw new ArgumentNullException(nameof(readSchemaAsync));
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        ProviderTargetSnapshot expected;
        try
        {
            expected = _targetSource.Read();
        }
        catch (InvalidOperationException)
        {
            throw new PersistencePlanExecutionException(
                PersistenceConsistencyFailureCodes.SchemaTargetMismatch,
                "platform.persistence");
        }

        if (expected.Provider is not PersistenceProvider.SqlServer)
        {
            throw new PersistencePlanExecutionException(
                PersistenceMigrationFailureCodes.ProviderUnsupported,
                "platform.persistence");
        }

        var actual = await _readSchemaAsync(cancellationToken);
        if (SqlServerProviderTargetComparer.FindFirstMismatch(expected, actual) is not null)
        {
            // 不把连接、SQL 或数据库返回值放入异常；readiness 只暴露稳定失败码。
            throw new PersistencePlanExecutionException(
                PersistenceConsistencyFailureCodes.SchemaTargetMismatch,
                "platform.persistence");
        }
    }
}

internal static class SqlServerSchemaSnapshotReader
{
    internal static IReadOnlyList<string> CommandTexts { get; } =
    [
        """
        SELECT
            s.name, t.name, c.column_id, c.name, ty.name, c.max_length,
            c.precision, c.scale, c.is_nullable, c.collation_name,
            cc.definition, cc.is_persisted, c.is_identity,
            CASE WHEN ty.name IN (N'timestamp', N'rowversion') THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
            dc.definition
        FROM sys.tables AS t
        INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        INNER JOIN sys.columns AS c ON c.object_id = t.object_id
        INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.computed_columns AS cc
            ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, c.column_id;
        """,
        """
        SELECT
            s.name, t.name, kc.name, kc.type, ic.key_ordinal, c.name
        FROM sys.key_constraints AS kc
        INNER JOIN sys.tables AS t ON t.object_id = kc.parent_object_id
        INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        INNER JOIN sys.index_columns AS ic
            ON ic.object_id = t.object_id AND ic.index_id = kc.unique_index_id
        INNER JOIN sys.columns AS c
            ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE t.is_ms_shipped = 0 AND ic.key_ordinal > 0
        ORDER BY s.name, t.name, kc.name, ic.key_ordinal;
        """,
        """
        SELECT
            ps.name, pt.name, fk.name, fkc.constraint_column_id,
            pc.name, rs.name, rt.name, rc.name, fk.delete_referential_action_desc
        FROM sys.foreign_keys AS fk
        INNER JOIN sys.tables AS pt ON pt.object_id = fk.parent_object_id
        INNER JOIN sys.schemas AS ps ON ps.schema_id = pt.schema_id
        INNER JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns AS pc
            ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        INNER JOIN sys.tables AS rt ON rt.object_id = fkc.referenced_object_id
        INNER JOIN sys.schemas AS rs ON rs.schema_id = rt.schema_id
        INNER JOIN sys.columns AS rc
            ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE pt.is_ms_shipped = 0
        ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id;
        """,
        """
        SELECT
            s.name, t.name, i.name, i.is_unique, i.type_desc, i.filter_definition,
            ic.key_ordinal, ic.is_descending_key, ic.is_included_column, ic.index_column_id, c.name
        FROM sys.indexes AS i
        INNER JOIN sys.tables AS t ON t.object_id = i.object_id
        INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        INNER JOIN sys.index_columns AS ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        INNER JOIN sys.columns AS c
            ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE t.is_ms_shipped = 0
            AND i.index_id > 0
            AND i.is_primary_key = 0
            AND i.is_unique_constraint = 0
            AND i.is_hypothetical = 0
        ORDER BY s.name, t.name, i.name, ic.is_included_column, ic.key_ordinal, ic.index_column_id;
        """,
        """
        SELECT s.name, t.name, cc.name, cc.definition
        FROM sys.check_constraints AS cc
        INNER JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
        INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, cc.name;
        """
    ];

    internal static async Task<SqlServerSchemaSnapshot> ReadAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var shouldClose = connection.State is not ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var columns = await ReadColumnsAsync(connection, cancellationToken);
            var keys = await ReadKeysAsync(connection, cancellationToken);
            var foreignKeys = await ReadForeignKeysAsync(connection, cancellationToken);
            var indexes = await ReadIndexesAsync(connection, cancellationToken);
            var checks = await ReadChecksAsync(connection, cancellationToken);
            var tableKeys = columns
                .Select(static column => new SqlServerTableKey(column.Schema, column.Table))
                .Distinct()
                .ToArray();

            return new SqlServerSchemaSnapshot(tableKeys.Select(key => new SqlServerTableSnapshot(
                key.Schema,
                key.Table,
                columns.Where(column => column.Matches(key)).OrderBy(static column => column.Ordinal).ToArray(),
                keys.Where(item => item.Matches(key) && item.Kind == "PK").SingleOrDefault(),
                keys.Where(item => item.Matches(key) && item.Kind == "UQ").ToArray(),
                foreignKeys.Where(item => item.Matches(key)).ToArray(),
                indexes.Where(item => item.Matches(key)).ToArray(),
                checks.Where(item => item.Matches(key)).ToArray())).ToArray());
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<IReadOnlyList<SqlServerColumnSnapshot>> ReadColumnsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CommandTexts[0];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<SqlServerColumnSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var typeName = reader.GetString(4);
            var rawMaxLength = reader.GetInt16(5);
            rows.Add(new SqlServerColumnSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                typeName,
                NormalizeMaxLength(typeName, rawMaxLength),
                reader.GetByte(6),
                reader.GetByte(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetBoolean(11),
                reader.GetBoolean(12),
                reader.GetBoolean(13),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<SqlServerKeySnapshot>> ReadKeysAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CommandTexts[1];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Schema, string Table, string Name, string Kind, int Ordinal, string Column)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetByte(4), reader.GetString(5)));
        }

        return rows.GroupBy(static row => (row.Schema, row.Table, row.Name, row.Kind))
            .Select(group => new SqlServerKeySnapshot(
                group.Key.Schema,
                group.Key.Table,
                group.Key.Name,
                group.Key.Kind,
                group.OrderBy(static row => row.Ordinal).Select(static row => row.Column).ToArray()))
            .ToArray();
    }

    private static async Task<IReadOnlyList<SqlServerForeignKeySnapshot>> ReadForeignKeysAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CommandTexts[2];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Schema, string Table, string Name, int Ordinal, string Column, string PrincipalSchema, string PrincipalTable, string PrincipalColumn, string OnDelete)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8)));
        }

        return rows.GroupBy(static row => (row.Schema, row.Table, row.Name, row.PrincipalSchema, row.PrincipalTable, row.OnDelete))
            .Select(group => new SqlServerForeignKeySnapshot(
                group.Key.Schema,
                group.Key.Table,
                group.Key.Name,
                group.OrderBy(static row => row.Ordinal).Select(static row => row.Column).ToArray(),
                group.Key.PrincipalSchema,
                group.Key.PrincipalTable,
                group.OrderBy(static row => row.Ordinal).Select(static row => row.PrincipalColumn).ToArray(),
                NormalizeReferentialAction(group.Key.OnDelete)))
            .ToArray();
    }

    private static async Task<IReadOnlyList<SqlServerIndexSnapshot>> ReadIndexesAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CommandTexts[3];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string Schema, string Table, string Name, bool Unique, string Type, string? Filter, int KeyOrdinal, bool Descending, bool Included, int IndexOrdinal, string Column)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetByte(6), reader.GetBoolean(7), reader.GetBoolean(8), reader.GetInt32(9), reader.GetString(10)));
        }

        return rows.GroupBy(static row => (row.Schema, row.Table, row.Name, row.Unique, row.Type, row.Filter))
            .Select(group => new SqlServerIndexSnapshot(
                group.Key.Schema,
                group.Key.Table,
                group.Key.Name,
                group.Key.Unique,
                group.Key.Type.StartsWith("CLUSTERED", StringComparison.Ordinal),
                group.Key.Filter,
                group.Where(static row => !row.Included).OrderBy(static row => row.KeyOrdinal).Select(static row => row.Column).ToArray(),
                group.Where(static row => !row.Included).OrderBy(static row => row.KeyOrdinal).Select(static row => row.Descending).ToArray(),
                group.Where(static row => row.Included).OrderBy(static row => row.IndexOrdinal).Select(static row => row.Column).ToArray()))
            .ToArray();
    }

    private static async Task<IReadOnlyList<SqlServerCheckSnapshot>> ReadChecksAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CommandTexts[4];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<SqlServerCheckSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SqlServerCheckSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }

    private static int? NormalizeMaxLength(string typeName, short rawMaxLength)
    {
        if (rawMaxLength < 0)
        {
            return null;
        }

        return typeName is "nvarchar" or "nchar" ? rawMaxLength / 2 : rawMaxLength;
    }

    private static string NormalizeReferentialAction(string value) => value switch
    {
        "CASCADE" => "Cascade",
        "SET_NULL" => "SetNull",
        "NO_ACTION" => "NoAction",
        "SET_DEFAULT" => "SetDefault",
        _ => value
    };
}

internal static class SqlServerProviderTargetComparer
{
    internal static string? FindFirstMismatch(
        ProviderTargetSnapshot expected,
        SqlServerSchemaSnapshot actual)
    {
        var expectedKeys = expected.Tables
            .Select(static table => new SqlServerTableKey(table.Schema, table.Name))
            .ToHashSet(SqlServerTableKeyComparer.Instance);
        var actualTables = actual.Tables.ToDictionary(
            static table => new SqlServerTableKey(table.Schema, table.Name),
            SqlServerTableKeyComparer.Instance);
        foreach (var expectedTable in expected.Tables)
        {
            var key = new SqlServerTableKey(expectedTable.Schema, expectedTable.Name);
            if (!actualTables.TryGetValue(key, out var actualTable))
            {
                return "table";
            }

            if (CompareTable(expectedTable, actualTable) is { } mismatch)
            {
                return mismatch;
            }
        }

        if (actual.Tables.Any(table =>
                !expectedKeys.Contains(new SqlServerTableKey(table.Schema, table.Name))
                && !IsPersistenceInfrastructureTable(table.Schema, table.Name)))
        {
            return "unexpected-table";
        }

        return null;
    }

    private static bool IsPersistenceInfrastructureTable(string schema, string name) =>
        string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(name, "__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "__NewPMigrationHistory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "__NewPDatabaseOwnership", StringComparison.OrdinalIgnoreCase));

    // 数据库物理列顺序不参与兼容性判断，其余表结构仍按既有规则严格比较。
    private static string? CompareTable(
        ProviderTableTarget expected,
        SqlServerTableSnapshot actual)
    {
        if (CompareColumns(expected.Columns, actual.Columns) is { } columnMismatch)
        {
            return columnMismatch;
        }

        if (!KeyEquals(expected.PrimaryKey, actual.PrimaryKey))
        {
            return "primary-key";
        }

        if (!SetEquals(expected.UniqueConstraints, actual.UniqueConstraints, KeyEquals))
        {
            return "unique-constraints";
        }

        if (!SetEquals(expected.ForeignKeys, actual.ForeignKeys, ForeignKeyEquals))
        {
            return "foreign-keys";
        }

        if (!SetEquals(expected.Indexes, actual.Indexes, IndexEquals))
        {
            return "indexes";
        }

        if (!SetEquals(expected.CheckConstraints, actual.CheckConstraints, CheckEquals))
        {
            return "check-constraints";
        }

        return null;
    }

    // 按稳定列名建立一一对应，避免把合法的 SQL Server physical ordinal 差异视为 drift。
    private static string? CompareColumns(
        IReadOnlyList<ProviderColumnTarget> expected,
        IReadOnlyList<SqlServerColumnSnapshot> actual)
    {
        if (expected.Count != actual.Count)
        {
            return "columns";
        }

        var actualByName = new Dictionary<string, SqlServerColumnSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var actualColumn in actual)
        {
            if (!actualByName.TryAdd(actualColumn.Name, actualColumn))
            {
                return "columns";
            }
        }

        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedColumn in expected)
        {
            if (!expectedNames.Add(expectedColumn.Name))
            {
                return "columns";
            }

            if (!actualByName.TryGetValue(expectedColumn.Name, out var actualColumn)
                || !ColumnEquals(expectedColumn, actualColumn))
            {
                return "column";
            }
        }

        return null;
    }

    // 列名配对后继续校验既有全部结构 facet，不让 ordinal 解耦弱化 schema drift 检测。
    private static bool ColumnEquals(ProviderColumnTarget expected, SqlServerColumnSnapshot actual) =>
        StoreTypesEqual(expected.StoreType, actual)
        && expected.IsNullable == actual.IsNullable
        && (expected.MaxLength is null || expected.MaxLength == actual.MaxLength)
        && (expected.Precision is null || expected.Precision == actual.Precision)
        && (expected.Scale is null || expected.Scale == actual.Scale)
        && (expected.IsUnicode is null || expected.IsUnicode == IsUnicode(actual.TypeName))
        && (expected.IsFixedLength is null || expected.IsFixedLength == IsFixedLength(actual.TypeName))
        && (expected.Collation is null || string.Equals(expected.Collation, actual.Collation, StringComparison.OrdinalIgnoreCase))
        && SqlEquals(expected.ComputedColumnSql, actual.ComputedSql)
        && (expected.IsStored is null || expected.IsStored == actual.IsStored)
        && expected.IsIdentity == actual.IsIdentity
        && expected.IsRowVersion == actual.IsRowVersion
        && DefaultEquals(expected, actual.DefaultSql);

    private static bool StoreTypesEqual(string expected, SqlServerColumnSnapshot actual) =>
        string.Equals(NormalizeStoreType(expected), NormalizeStoreType(BuildStoreType(actual)), StringComparison.Ordinal);

    private static string BuildStoreType(SqlServerColumnSnapshot column)
    {
        var type = column.TypeName.ToLowerInvariant();
        if (type is "nvarchar" or "nchar" or "varchar" or "char" or "varbinary" or "binary")
        {
            return column.MaxLength is null ? $"{type}(max)" : $"{type}({column.MaxLength})";
        }

        if (type is "decimal" or "numeric")
        {
            return $"{type}({column.Precision},{column.Scale})";
        }

        if (type is "datetime2" or "datetimeoffset" or "time" && column.Scale != 7)
        {
            return $"{type}({column.Scale})";
        }

        return type;
    }

    private static string NormalizeStoreType(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), "\\s+", string.Empty, RegexOptions.CultureInvariant);

    private static bool? IsUnicode(string typeName) => typeName.ToLowerInvariant() switch
    {
        "nvarchar" or "nchar" or "ntext" => true,
        "varchar" or "char" or "text" => false,
        _ => null
    };

    private static bool? IsFixedLength(string typeName) => typeName.ToLowerInvariant() switch
    {
        "nchar" or "char" or "binary" => true,
        "nvarchar" or "varchar" or "varbinary" => false,
        _ => null
    };

    private static bool DefaultEquals(ProviderColumnTarget expected, string? actual)
    {
        if (expected.DefaultValue is null && expected.DefaultValueSql is null)
        {
            return actual is null;
        }

        return SqlEquals(expected.DefaultValueSql ?? expected.DefaultValue, actual);
    }

    private static bool KeyEquals(ProviderKeyTarget? expected, SqlServerKeySnapshot? actual) =>
        expected is null
            ? actual is null
            : actual is not null
                && string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase)
                && SequenceEquals(expected.Columns, actual.Columns);

    private static bool ForeignKeyEquals(ProviderForeignKeyTarget expected, SqlServerForeignKeySnapshot actual) =>
        string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase)
        && SequenceEquals(expected.Columns, actual.Columns)
        && string.Equals(expected.PrincipalSchema, actual.PrincipalSchema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expected.PrincipalTable, actual.PrincipalTable, StringComparison.OrdinalIgnoreCase)
        && SequenceEquals(expected.PrincipalColumns, actual.PrincipalColumns)
        && string.Equals(
            NormalizeExpectedReferentialAction(expected.OnDelete),
            actual.OnDelete,
            StringComparison.Ordinal);

    private static string NormalizeExpectedReferentialAction(string value) =>
        string.Equals(value, "Restrict", StringComparison.Ordinal)
            ? "NoAction"
            : value;

    private static bool IndexEquals(ProviderIndexTarget expected, SqlServerIndexSnapshot actual)
    {
        var expectedDescending = expected.IsDescending ?? Enumerable.Repeat(false, expected.Columns.Count).ToArray();
        var expectedClustered = expected.ProviderAnnotations.Any(annotation =>
            string.Equals(annotation.Name, "SqlServer:Clustered", StringComparison.Ordinal)
            && string.Equals(annotation.Value, "True", StringComparison.OrdinalIgnoreCase));
        var expectsIncludedColumns = expected.ProviderAnnotations.Any(annotation =>
            string.Equals(annotation.Name, "SqlServer:Include", StringComparison.Ordinal));
        return string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase)
            && expected.IsUnique == actual.IsUnique
            && SequenceEquals(expected.Columns, actual.Columns)
            && expectedDescending.SequenceEqual(actual.IsDescending)
            && SqlEquals(expected.Filter, actual.Filter)
            && expectedClustered == actual.IsClustered
            && (expectsIncludedColumns || actual.IncludedColumns.Count == 0);
    }

    private static bool CheckEquals(ProviderCheckConstraintTarget expected, SqlServerCheckSnapshot actual) =>
        string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase)
        && SqlEquals(expected.Sql, actual.Sql);

    private static bool SetEquals<TExpected, TActual>(
        IReadOnlyList<TExpected> expected,
        IReadOnlyList<TActual> actual,
        Func<TExpected, TActual, bool> equals)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        var unmatched = actual.ToList();
        foreach (var item in expected)
        {
            var index = unmatched.FindIndex(candidate => equals(item, candidate));
            if (index < 0)
            {
                return false;
            }

            unmatched.RemoveAt(index);
        }

        return unmatched.Count == 0;
    }

    private static bool SequenceEquals(IReadOnlyList<string> expected, IReadOnlyList<string> actual) =>
        expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase);

    private static bool SqlEquals(string? expected, string? actual) =>
        string.Equals(NormalizeSql(expected), NormalizeSql(actual), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeSql(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value.Trim(), "\\s+", " ", RegexOptions.CultureInvariant);
        while (normalized.Length > 1 && normalized[0] == '(' && normalized[^1] == ')')
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized;
    }
}

internal sealed record SqlServerSchemaSnapshot(IReadOnlyList<SqlServerTableSnapshot> Tables);

internal sealed record SqlServerTableSnapshot(
    string Schema,
    string Name,
    IReadOnlyList<SqlServerColumnSnapshot> Columns,
    SqlServerKeySnapshot? PrimaryKey,
    IReadOnlyList<SqlServerKeySnapshot> UniqueConstraints,
    IReadOnlyList<SqlServerForeignKeySnapshot> ForeignKeys,
    IReadOnlyList<SqlServerIndexSnapshot> Indexes,
    IReadOnlyList<SqlServerCheckSnapshot> CheckConstraints);

internal sealed record SqlServerColumnSnapshot(
    string Schema,
    string Table,
    int Ordinal,
    string Name,
    string TypeName,
    int? MaxLength,
    int Precision,
    int Scale,
    bool IsNullable,
    string? Collation,
    string? ComputedSql,
    bool? IsStored,
    bool IsIdentity,
    bool IsRowVersion,
    string? DefaultSql)
{
    public bool Matches(SqlServerTableKey key) => key.Matches(Schema, Table);
}

internal sealed record SqlServerKeySnapshot(
    string Schema,
    string Table,
    string Name,
    string Kind,
    IReadOnlyList<string> Columns)
{
    public bool Matches(SqlServerTableKey key) => key.Matches(Schema, Table);
}

internal sealed record SqlServerForeignKeySnapshot(
    string Schema,
    string Table,
    string Name,
    IReadOnlyList<string> Columns,
    string PrincipalSchema,
    string PrincipalTable,
    IReadOnlyList<string> PrincipalColumns,
    string OnDelete)
{
    public bool Matches(SqlServerTableKey key) => key.Matches(Schema, Table);
}

internal sealed record SqlServerIndexSnapshot(
    string Schema,
    string Table,
    string Name,
    bool IsUnique,
    bool IsClustered,
    string? Filter,
    IReadOnlyList<string> Columns,
    IReadOnlyList<bool> IsDescending,
    IReadOnlyList<string> IncludedColumns)
{
    public bool Matches(SqlServerTableKey key) => key.Matches(Schema, Table);
}

internal sealed record SqlServerCheckSnapshot(string Schema, string Table, string Name, string Sql)
{
    public bool Matches(SqlServerTableKey key) => key.Matches(Schema, Table);
}

internal sealed record SqlServerTableKey(string Schema, string Table)
{
    public bool Matches(string schema, string table) =>
        string.Equals(Schema, schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Table, table, StringComparison.OrdinalIgnoreCase);
}

internal sealed class SqlServerTableKeyComparer : IEqualityComparer<SqlServerTableKey>
{
    public static SqlServerTableKeyComparer Instance { get; } = new();

    public bool Equals(SqlServerTableKey? x, SqlServerTableKey? y) =>
        x is not null && y is not null && x.Matches(y.Schema, y.Table);

    public int GetHashCode(SqlServerTableKey obj) => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
        StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table));
}
