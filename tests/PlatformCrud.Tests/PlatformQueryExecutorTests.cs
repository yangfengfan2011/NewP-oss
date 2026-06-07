using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class PlatformQueryExecutorTests
{
    [Fact]
    public async Task QueryAsync_AppliesFilterSortPagingAndProjectionInSqlShape()
    {
        var sql = new List<string>();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = BuildDbContext(connection, sql);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
            Row("Person_1", "P001", "张三", "FullTime", true),
            Row("Person_2", "P002", "李四", "FullTime", true),
            Row("Person_3", "X003", "王五", "PartTime", true));
        await dbContext.SaveChangesAsync();
        sql.Clear();

        var executor = new EfPlatformQueryExecutor(dbContext);
        var result = await executor.QueryAsync(
            Descriptor(),
            new PlatformQueryRequest(
                Fields: ["EID", "PersonNo", "CompanyName"],
                Page: 1,
                PageSize: 1,
                SortBy: "PersonNo",
                SortDirection: "desc",
                Conditions: [new PlatformQueryCondition("EmploymentStatus", "=", "FullTime")]));

        Assert.Equal(2, result.Total);
        var row = Assert.Single(result.Items);
        Assert.Equal("P002", row["PersonNo"]);
        Assert.True(row.ContainsKey("CompanyId"));
        Assert.False(row.ContainsKey("CreatedAt"));

        var executedSql = string.Join(Environment.NewLine, sql);
        Assert.Contains("PersonNo", executedSql, StringComparison.Ordinal);
        Assert.Contains("CompanyId", executedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAt", executedSql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", executedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QueryAsync_WhenConditionGroupUsesOr_ReturnsRowsMatchingAnyCondition()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = BuildDbContext(connection, []);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
            Row("Person_1", "P001", "张三", "FullTime", true),
            Row("Person_2", "X002", "李四", "FullTime", true));
        await dbContext.SaveChangesAsync();

        var result = await new EfPlatformQueryExecutor(dbContext).QueryAsync(
            Descriptor(),
            new PlatformQueryRequest(
                Fields: ["EID", "PersonNo"],
                Conditions:
                [
                    new PlatformQueryCondition("PersonNo", "contains", "P", GroupKey: "keyword", GroupOperator: "or"),
                    new PlatformQueryCondition("ENAME", "contains", "李", GroupKey: "keyword", GroupOperator: "or")
                ]));

        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task QueryAsync_WhenConditionGroupMixesOperators_ThrowsStableCode()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = BuildDbContext(connection, []);
        await dbContext.Database.EnsureCreatedAsync();

        var error = await Assert.ThrowsAsync<PlatformQueryValidationException>(() =>
            new EfPlatformQueryExecutor(dbContext).QueryAsync(
                Descriptor(),
                new PlatformQueryRequest(
                    Fields: ["EID"],
                    Conditions:
                    [
                        new PlatformQueryCondition("PersonNo", "contains", "P", GroupKey: "keyword", GroupOperator: "or"),
                        new PlatformQueryCondition("ENAME", "contains", "李", GroupKey: "keyword", GroupOperator: "and")
                    ])));

        Assert.Equal("PlatformQuery.MixedConditionGroupOperator", error.Code);
    }

    private static EfPlatformDbContext BuildDbContext(SqliteConnection connection, List<string> sql)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlite(connection)
            .LogTo(sql.Add, LogLevel.Information)
            .EnableSensitiveDataLogging()
            .Options;

        return new EfPlatformDbContext(options, [PlatformCrudModelDefinitions.Person()]);
    }

    private static Dictionary<string, object?> Row(string id, string personNo, string name, string status, bool isEnabled) => new(StringComparer.Ordinal)
    {
        ["EID"] = id,
        ["PersonNo"] = personNo,
        ["ENAME"] = name,
        ["CompanyId"] = "Company_1",
        ["DepartmentId"] = "Department_1",
        ["PositionId"] = "Position_1",
        ["EmploymentStatus"] = status,
        ["IsEnabled"] = isEnabled,
        ["CreatedAt"] = DateTime.UtcNow,
        ["UpdatedAt"] = DateTime.UtcNow
    };

    private static PlatformQueryDescriptor Descriptor() => new()
    {
        ResourceKey = "organization.person",
        ModelName = "Person",
        IdField = "EID",
        AllowedModelFields = ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"],
        DefaultFields = ["EID", "PersonNo", "ENAME"],
        DefaultSortField = "ENAME",
        DisplayValueFields =
        [
            new PlatformDisplayValueField("CompanyName", "CompanyId", "Company", "EID", "ENAME")
        ]
    };
}
