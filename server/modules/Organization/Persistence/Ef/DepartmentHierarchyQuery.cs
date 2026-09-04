using System.Data;
using System.Data.Common;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

internal enum DepartmentParentRelationship
{
    Allowed,
    Self,
    Descendant,
    Corrupted
}

internal sealed record DepartmentHierarchyCandidatePage(
    IReadOnlyList<Department> Items,
    bool HasMore);

internal interface IDepartmentHierarchyQuery
{
    // 在数据库侧沿候选父级的祖先链完成单值关系裁决。
    Task<DepartmentParentRelationship> ClassifyParentAsync(
        string companyId,
        string currentDepartmentId,
        string candidateParentId,
        CancellationToken cancellationToken);

    // 在数据库侧递归排除后代并返回有界候选页。
    Task<DepartmentHierarchyCandidatePage> LookupParentCandidatesAsync(
        string companyId,
        string currentDepartmentId,
        string? keyword,
        int page,
        int pageSize,
        bool includeDisabled,
        CancellationToken cancellationToken);
}

/// <summary>
/// 以 SQL Server recursive CTE 在数据库侧完成无业务容量上限的部门层级裁决。
/// </summary>
internal sealed class SqlServerDepartmentHierarchyQuery(EfPlatformDbContext dbContext)
    : IDepartmentHierarchyQuery
{
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    private const string ClassifyParentSql = """
        ;WITH [Ancestors] AS
        (
            SELECT [department].[EID],
                   [department].[ParentDepartmentId],
                   CAST(N'|' + REPLACE([department].[EID], N'|', N'||') + N'|' AS nvarchar(max)) AS [Path],
                   CAST(0 AS bit) AS [IsCycle],
                   0 AS [Depth]
            FROM [Department] AS [department]
            WHERE [department].[CompanyId] = @companyId
              AND [department].[EID] = @candidateParentId

            UNION ALL

            SELECT [parent].[EID],
                   [parent].[ParentDepartmentId],
                   CAST([ancestor].[Path] + REPLACE([parent].[EID], N'|', N'||') + N'|' AS nvarchar(max)),
                   CAST(CASE WHEN CHARINDEX(
                       N'|' + REPLACE([parent].[EID], N'|', N'||') + N'|',
                       [ancestor].[Path]) > 0 THEN 1 ELSE 0 END AS bit),
                   [ancestor].[Depth] + 1
            FROM [Department] AS [parent]
            INNER JOIN [Ancestors] AS [ancestor]
                ON [parent].[EID] = [ancestor].[ParentDepartmentId]
            WHERE [parent].[CompanyId] = @companyId
              AND [ancestor].[IsCycle] = 0
        )
        SELECT CASE
            WHEN EXISTS (
                SELECT 1 FROM [Ancestors]
                WHERE [Depth] = 0 AND [EID] = @currentDepartmentId) THEN 1
            WHEN EXISTS (
                SELECT 1 FROM [Ancestors]
                WHERE [Depth] > 0 AND [EID] = @currentDepartmentId) THEN 2
            WHEN EXISTS (
                SELECT 1 FROM [Ancestors]
                WHERE [IsCycle] = 1) THEN 3
            WHEN EXISTS (
                SELECT 1
                FROM [Ancestors] AS [ancestor]
                WHERE [ancestor].[IsCycle] = 0
                  AND [ancestor].[ParentDepartmentId] IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [Department] AS [parent]
                      WHERE [parent].[CompanyId] = @companyId
                        AND [parent].[EID] = [ancestor].[ParentDepartmentId])) THEN 3
            ELSE 0
        END
        OPTION (MAXRECURSION 0);
        """;

    private const string LookupParentCandidatesSql = """
        CREATE TABLE #DepartmentDescendants
        (
            [EID] nvarchar(450) NOT NULL,
            [IsCycle] bit NOT NULL
        );

        ;WITH [Descendants] AS
        (
            SELECT [department].[EID],
                   CAST(N'|' + REPLACE([department].[EID], N'|', N'||') + N'|' AS nvarchar(max)) AS [Path],
                   CAST(0 AS bit) AS [IsCycle]
            FROM [Department] AS [department]
            WHERE [department].[CompanyId] = @companyId
              AND [department].[EID] = @currentDepartmentId

            UNION ALL

            SELECT [child].[EID],
                   CAST([descendant].[Path] + REPLACE([child].[EID], N'|', N'||') + N'|' AS nvarchar(max)),
                   CAST(CASE WHEN CHARINDEX(
                       N'|' + REPLACE([child].[EID], N'|', N'||') + N'|',
                       [descendant].[Path]) > 0 THEN 1 ELSE 0 END AS bit)
            FROM [Department] AS [child]
            INNER JOIN [Descendants] AS [descendant]
                ON [child].[ParentDepartmentId] = [descendant].[EID]
            WHERE [child].[CompanyId] = @companyId
              AND [descendant].[IsCycle] = 0
        )
        INSERT INTO #DepartmentDescendants ([EID], [IsCycle])
        SELECT [EID], [IsCycle]
        FROM [Descendants]
        OPTION (MAXRECURSION 0);

        SELECT CASE WHEN EXISTS (
            SELECT 1 FROM #DepartmentDescendants WHERE [IsCycle] = 1)
            THEN 1 ELSE 0 END AS [IsCorrupted];

        SELECT [department].[EID],
               [department].[CompanyId],
               [department].[ParentDepartmentId],
               [department].[ECODE],
               [department].[ENAME],
               [department].[IsEnabled]
        FROM [Department] AS [department]
        WHERE [department].[CompanyId] = @companyId
          AND (@includeDisabled = 1 OR [department].[IsEnabled] = 1)
          AND (@keyword IS NULL
               OR CHARINDEX(@keyword, [department].[ECODE]) > 0
               OR CHARINDEX(@keyword, [department].[ENAME]) > 0)
          AND NOT EXISTS (
              SELECT 1
              FROM #DepartmentDescendants AS [descendant]
              WHERE [descendant].[EID] = [department].[EID])
        ORDER BY [department].[ENAME], [department].[EID]
        OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY;

        DROP TABLE #DepartmentDescendants;
        """;

    // 祖先关系只返回单个枚举值，递归工作量留在数据库且查询次数不随深度增长。
    public Task<DepartmentParentRelationship> ClassifyParentAsync(
        string companyId,
        string currentDepartmentId,
        string candidateParentId,
        CancellationToken cancellationToken) => ExecuteAsync(
        ClassifyParentSql,
        new Dictionary<string, object?>
        {
            ["@companyId"] = companyId,
            ["@currentDepartmentId"] = currentDepartmentId,
            ["@candidateParentId"] = candidateParentId
        },
        async (reader, token) =>
        {
            if (!await reader.ReadAsync(token))
            {
                throw new InvalidOperationException("Department hierarchy classification returned no result.");
            }

            return (DepartmentParentRelationship)reader.GetInt32(0);
        },
        cancellationToken);

    // CTE 与分页在同一数据库命令内完成；应用层最多保留 pageSize + 1 行。
    public Task<DepartmentHierarchyCandidatePage> LookupParentCandidatesAsync(
        string companyId,
        string currentDepartmentId,
        string? keyword,
        int page,
        int pageSize,
        bool includeDisabled,
        CancellationToken cancellationToken) => ExecuteAsync(
        LookupParentCandidatesSql,
        new Dictionary<string, object?>
        {
            ["@companyId"] = companyId,
            ["@currentDepartmentId"] = currentDepartmentId,
            ["@keyword"] = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(),
            ["@includeDisabled"] = includeDisabled,
            ["@offset"] = (page - 1) * pageSize,
            ["@take"] = pageSize + 1
        },
        async (reader, token) =>
        {
            if (!await reader.ReadAsync(token))
            {
                throw new InvalidOperationException("Department hierarchy state returned no result.");
            }

            var corrupted = reader.GetInt32(0) == 1;
            if (!await reader.NextResultAsync(token))
            {
                throw new InvalidOperationException("Department hierarchy candidate page returned no result set.");
            }

            var items = new List<Department>(pageSize + 1);
            while (await reader.ReadAsync(token))
            {
                items.Add(new Department
                {
                    Id = reader.GetString(0),
                    CompanyId = reader.GetString(1),
                    ParentDepartmentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Code = reader.GetString(3),
                    Name = reader.GetString(4),
                    IsEnabled = reader.GetBoolean(5)
                });
            }

            if (corrupted)
            {
                throw new DepartmentHierarchyCorruptedException();
            }

            return new DepartmentHierarchyCandidatePage(
                items.Take(pageSize).ToArray(),
                items.Count > pageSize);
        },
        cancellationToken);

    // Organization 当前唯一迁移流与受支持 profile 均为 SQL Server；其他 provider 不走隐式降级路径。
    private async Task<T> ExecuteAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        Func<DbDataReader, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(dbContext.Database.ProviderName, SqlServerProviderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Organization department hierarchy queries require SQL Server, but provider '{dbContext.Database.ProviderName}' is active.");
        }

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (dbContext.Database.CurrentTransaction is { } transaction)
            {
                command.Transaction = transaction.GetDbTransaction();
            }

            foreach (var parameter in parameters)
            {
                AddParameter(command, parameter.Key, parameter.Value);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await read(reader, cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    // 参数始终经 provider command 创建，层级标识和搜索词不进入 SQL 文本。
    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
