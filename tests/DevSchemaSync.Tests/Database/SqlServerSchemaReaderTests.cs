using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Persistence.DevSync.Database;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Database;

public sealed class SqlServerSchemaReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsTableAndColumnSummaryFromRows()
    {
        CancellationToken? capturedToken = null;
        var reader = new SqlServerSchemaReader((_, cancellationToken) =>
        {
            capturedToken = cancellationToken;
            return Task.FromResult<IReadOnlyList<SqlServerSchemaReader.ColumnRow>>(
        [
            new("dbo", "Users", "Id", "int", false),
            new("dbo", "Users", "DisplayName", "nvarchar", true),
            new("dbo", "Orders", "Id", "bigint", false)
        ]);
        });

        using var cts = new CancellationTokenSource();

        var schema = await reader.ReadAsync("Server=(local);Database=fake;", cts.Token);

        Assert.Equal(2, schema.Tables.Count);
        Assert.Equal(cts.Token, capturedToken);

        var users = Assert.Single(schema.Tables, static table => table.Name == "Users");
        Assert.Equal("dbo", users.Schema);
        Assert.Collection(
            users.Columns,
            column =>
            {
                Assert.Equal("Id", column.Name);
                Assert.Equal("int", column.StoreType);
                Assert.False(column.IsNullable);
            },
            column =>
            {
                Assert.Equal("DisplayName", column.Name);
                Assert.Equal("nvarchar", column.StoreType);
                Assert.True(column.IsNullable);
            });
    }

    [Fact]
    public async Task ReadAsync_UsingDevelopmentConnection_ReturnsSchemaObject()
    {
        // 集成数据库不是单元测试前置条件；只有显式开启时才连接开发库，避免无 SQL Server 环境被计为失败。
        if (!string.Equals(
            Environment.GetEnvironmentVariable("DEV_SCHEMA_SYNC_SQLSERVER_TESTS"),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        var repositoryRoot = RepositoryRootLocator.Find();
        var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot, "Development");
        var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration, repositoryRoot);
        var reader = new SqlServerSchemaReader();
        var schema = await reader.ReadAsync(context.ConnectionString);

        Assert.NotNull(schema);
        Assert.NotEmpty(schema.Tables);
        Assert.Contains(schema.Tables, static table => !string.IsNullOrWhiteSpace(table.Name));
        Assert.Contains(schema.Tables, static table => table.Columns.Count > 0);
    }
}
