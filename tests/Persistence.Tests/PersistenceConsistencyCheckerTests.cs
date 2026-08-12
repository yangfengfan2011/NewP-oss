using System.Text.RegularExpressions;
using ICP.Platform.Persistence.EfCore.ProviderTargets;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PersistenceConsistencyCheckerTests
{
    [Fact]
    public void FindFirstMismatch_WhenSchemaMatchesProviderTarget_ReturnsNull()
    {
        var target = Target();

        var mismatch = SqlServerProviderTargetComparer.FindFirstMismatch(target, Snapshot());

        Assert.Null(mismatch);
        Assert.Equal(64, ProviderTargetDigest.Compute(target).Length);
    }

    [Theory]
    [InlineData("missing-table")]
    [InlineData("column-nullability")]
    [InlineData("column-default")]
    [InlineData("primary-key")]
    [InlineData("foreign-key")]
    [InlineData("index")]
    [InlineData("unexpected-table")]
    public void FindFirstMismatch_WhenActualSchemaDrifts_ReturnsStableTargetMismatch(string drift)
    {
        var snapshot = Snapshot();
        snapshot = drift switch
        {
            "missing-table" => snapshot with { Tables = [] },
            "column-nullability" => ReplaceAsset(snapshot, table => table with
            {
                Columns = table.Columns.Select(column => column.Name == "Code"
                    ? column with { IsNullable = true }
                    : column).ToArray()
            }),
            "column-default" => ReplaceAsset(snapshot, table => table with
            {
                Columns = table.Columns.Select(column => column.Name == "Code"
                    ? column with { DefaultSql = "(N'generated')" }
                    : column).ToArray()
            }),
            "primary-key" => ReplaceAsset(snapshot, table => table with { PrimaryKey = null }),
            "foreign-key" => ReplaceAsset(snapshot, table => table with { ForeignKeys = [] }),
            "index" => ReplaceAsset(snapshot, table => table with { Indexes = [] }),
            "unexpected-table" => snapshot with
            {
                Tables = [.. snapshot.Tables, EmptyTable("Unmanaged")]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(drift), drift, null)
        };

        var mismatch = SqlServerProviderTargetComparer.FindFirstMismatch(Target(), snapshot);

        Assert.NotNull(mismatch);
    }

    [Fact]
    public void FindFirstMismatch_WhenActualSchemaContainsPersistenceInfrastructure_ReturnsNull()
    {
        var snapshot = Snapshot() with
        {
            Tables =
            [
                .. Snapshot().Tables,
                EmptyTable("__EFMigrationsHistory"),
                EmptyTable("__NewPMigrationHistory"),
                EmptyTable("__NewPDatabaseOwnership")
            ]
        };

        Assert.Null(SqlServerProviderTargetComparer.FindFirstMismatch(Target(), snapshot));
    }

    [Fact]
    public async Task CheckAsync_WhenSchemaDrifts_ThrowsSanitizedStableFailure()
    {
        var target = Target();
        var checker = new SqlServerProviderTargetConsistencyChecker(
            new FixedTargetSource(target),
            _ => Task.FromResult(Snapshot() with { Tables = [] }));

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() =>
            checker.CheckAsync(CancellationToken.None));

        Assert.Equal(PersistenceConsistencyFailureCodes.SchemaTargetMismatch, exception.FailureCode);
        Assert.DoesNotContain("Server=", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_ConsumesCurrentProviderTargetWithoutMigrationReplay()
    {
        var currentTarget = Target() with
        {
            Tables = Target().Tables.Select(table => table.Name == "Asset"
                ? table with
                {
                    Columns = table.Columns.Select(column => column.Name == "Code"
                        ? column with { IsNullable = true }
                        : column).ToArray()
                }
                : table).ToArray()
        };
        var checker = new SqlServerProviderTargetConsistencyChecker(
            new FixedTargetSource(currentTarget),
            _ => Task.FromResult(Snapshot()));

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() =>
            checker.CheckAsync(CancellationToken.None));

        Assert.Equal(PersistenceConsistencyFailureCodes.SchemaTargetMismatch, exception.FailureCode);
    }

    [Fact]
    public async Task CheckAsync_WhenCurrentProviderTargetCannotBeRead_ThrowsSanitizedStableFailure()
    {
        var checker = new SqlServerProviderTargetConsistencyChecker(
            new ThrowingTargetSource(),
            _ => Task.FromResult(Snapshot()));

        var exception = await Assert.ThrowsAsync<PersistencePlanExecutionException>(() =>
            checker.CheckAsync(CancellationToken.None));

        Assert.Equal(PersistenceConsistencyFailureCodes.SchemaTargetMismatch, exception.FailureCode);
        Assert.DoesNotContain("Server=secret", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogReader_ContainsOnlyReadStatements()
    {
        var writePattern = new Regex(
            @"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Assert.NotEmpty(SqlServerSchemaSnapshotReader.CommandTexts);
        Assert.All(SqlServerSchemaSnapshotReader.CommandTexts, sql =>
        {
            Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(writePattern, sql);
        });
    }

    private static ProviderTargetSnapshot Target()
    {
        var id = Column("Id");
        var code = Column("Code");
        var parent = new ProviderTableTarget(
            "Example",
            "dbo",
            "Parent",
            [id],
            new ProviderKeyTarget("PK_Parent", ["Id"]),
            [],
            [],
            [],
            []);
        var asset = new ProviderTableTarget(
            "Example",
            "dbo",
            "Asset",
            [id, code],
            new ProviderKeyTarget("PK_Asset", ["Id"]),
            [],
            [new ProviderForeignKeyTarget("FK_Asset_Parent", ["Id"], "Example", "dbo", "Parent", ["Id"], "NoAction")],
            [new ProviderIndexTarget("IX_Asset_Code", ["Code"], false, null, null, [])],
            []);
        return new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [parent, asset]);
    }

    private static ProviderColumnTarget Column(string name) => new(
        name,
        "nvarchar(128)",
        typeof(string).FullName!,
        IsNullable: false,
        MaxLength: 128,
        Precision: null,
        Scale: null,
        IsUnicode: null,
        IsFixedLength: null,
        Collation: null,
        ComputedColumnSql: null,
        IsStored: null,
        IsIdentity: false,
        IsRowVersion: false,
        DefaultValue: null,
        DefaultValueSql: null,
        ProviderAnnotations: []);

    private static SqlServerSchemaSnapshot Snapshot()
    {
        var parent = new SqlServerTableSnapshot(
            "dbo",
            "Parent",
            [ActualColumn("Parent", 1, "Id")],
            new SqlServerKeySnapshot("dbo", "Parent", "PK_Parent", "PK", ["Id"]),
            [],
            [],
            [],
            []);
        var asset = new SqlServerTableSnapshot(
            "dbo",
            "Asset",
            [ActualColumn("Asset", 1, "Id"), ActualColumn("Asset", 2, "Code")],
            new SqlServerKeySnapshot("dbo", "Asset", "PK_Asset", "PK", ["Id"]),
            [],
            [new SqlServerForeignKeySnapshot("dbo", "Asset", "FK_Asset_Parent", ["Id"], "dbo", "Parent", ["Id"], "NoAction")],
            [new SqlServerIndexSnapshot("dbo", "Asset", "IX_Asset_Code", false, false, null, ["Code"], [false], [])],
            []);
        return new SqlServerSchemaSnapshot([parent, asset]);
    }

    private static SqlServerTableSnapshot EmptyTable(string name) => new(
        "dbo",
        name,
        [],
        null,
        [],
        [],
        [],
        []);

    private static SqlServerColumnSnapshot ActualColumn(string table, int ordinal, string name) => new(
        "dbo",
        table,
        ordinal,
        name,
        "nvarchar",
        128,
        Precision: 0,
        Scale: 0,
        IsNullable: false,
        Collation: null,
        ComputedSql: null,
        IsStored: null,
        IsIdentity: false,
        IsRowVersion: false,
        DefaultSql: null);

    private static SqlServerSchemaSnapshot ReplaceAsset(
        SqlServerSchemaSnapshot snapshot,
        Func<SqlServerTableSnapshot, SqlServerTableSnapshot> replace) =>
        snapshot with
        {
            Tables = snapshot.Tables.Select(table => table.Name == "Asset" ? replace(table) : table).ToArray()
        };

    private sealed class FixedTargetSource(ProviderTargetSnapshot target) : IProviderTargetSource
    {
        public ProviderTargetSnapshot Read() => target;
    }

    private sealed class ThrowingTargetSource : IProviderTargetSource
    {
        public ProviderTargetSnapshot Read() =>
            throw new InvalidOperationException("Server=secret; provider target projection failed.");
    }
}
