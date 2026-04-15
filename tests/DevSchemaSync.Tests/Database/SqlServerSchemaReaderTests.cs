using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Tools.DevSchemaSync.Database;
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
        var repositoryRoot = RepositoryRootLocator.Find();
        var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot, "Development");
        var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration, repositoryRoot);
        var reader = new SqlServerSchemaReader();
        SqlServerSchemaReader.DatabaseSchema schema;

        try
        {
            schema = await reader.ReadAsync(context.ConnectionString);
        }
        catch (System.Data.Common.DbException exception)
        {
            throw Xunit.Sdk.SkipException.ForSkip($"Development SqlServer 前置条件不满足，已尝试连接: {context.ConnectionString}。失败原因: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            throw Xunit.Sdk.SkipException.ForSkip($"Development SqlServer 前置条件不满足，已尝试连接: {context.ConnectionString}。失败原因: {exception.Message}");
        }

        Assert.NotNull(schema);
        Assert.NotEmpty(schema.Tables);
        Assert.Contains(schema.Tables, static table => !string.IsNullOrWhiteSpace(table.Name));
        Assert.Contains(schema.Tables, static table => table.Columns.Count > 0);
    }
}
