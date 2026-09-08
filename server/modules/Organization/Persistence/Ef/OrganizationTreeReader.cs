using System.Data;
using System.Data.Common;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

public sealed record OrganizationTreeNode(string NodeKey, string NodeType, string Label,
    bool HasChildren, string Code, bool IsEnabled, IReadOnlyDictionary<string, object?> Context);
public sealed record OrganizationTreeSearchItem(OrganizationTreeNode Node, IReadOnlyList<OrganizationTreeNode> Path);
public sealed record OrganizationTreeSearchPage(IReadOnlyList<OrganizationTreeSearchItem> Items, int Total, int Page, int PageSize);

/// <summary>Organization 拥有的组织树投影；四类读取共用节点身份与数据库侧有序查询。</summary>
public sealed class OrganizationTreeReader(EfPlatformDbContext dbContext)
{
    private const string Nodes = """
        WITH Nodes AS (
          SELECT CAST(N'company:' + c.EID AS nvarchar(500)) AS NodeKey, CAST(NULL AS nvarchar(500)) AS ParentKey,
            CAST(N'company' AS nvarchar(20)) AS NodeType, c.EID AS Id, c.EID AS CompanyId,
            CAST(NULL AS nvarchar(450)) AS ParentDepartmentId, c.ENAME AS DisplayName, c.ECODE AS Code,
            c.IsEnabled, c.SortOrder,
            CAST(CASE WHEN EXISTS(SELECT 1 FROM Department d WHERE d.CompanyId=c.EID AND d.ParentDepartmentId IS NULL) THEN 1 ELSE 0 END AS bit) AS HasChildren
          FROM Company c
          UNION ALL
          SELECT CAST(N'department:' + d.EID AS nvarchar(500)),
            CAST(CASE WHEN d.ParentDepartmentId IS NULL THEN N'company:' + d.CompanyId ELSE N'department:' + d.ParentDepartmentId END AS nvarchar(500)),
            CAST(N'department' AS nvarchar(20)), d.EID, d.CompanyId, d.ParentDepartmentId, d.ENAME, d.ECODE,
            d.IsEnabled, d.SortOrder,
            CAST(CASE WHEN EXISTS(SELECT 1 FROM Department ch WHERE ch.ParentDepartmentId=d.EID AND ch.CompanyId=d.CompanyId) THEN 1 ELSE 0 END AS bit)
          FROM Department d
        )
        """;
    private const string PathProjection = """
        , Ancestors AS (
          SELECT s.NodeKey AS TargetKey, n.*, 0 AS Depth,
            CAST(N'|' + REPLACE(n.NodeKey,N'|',N'||') + N'|' AS nvarchar(max)) AS Visited, CAST(0 AS bit) AS IsCycle
          FROM Seeds s JOIN Nodes n ON n.NodeKey=s.NodeKey
          UNION ALL
          SELECT a.TargetKey, n.*, a.Depth+1,
            CAST(a.Visited+REPLACE(n.NodeKey,N'|',N'||')+N'|' AS nvarchar(max)),
            CAST(CASE WHEN CHARINDEX(N'|'+REPLACE(n.NodeKey,N'|',N'||')+N'|',a.Visited)>0 THEN 1 ELSE 0 END AS bit)
          FROM Ancestors a JOIN Nodes n ON n.NodeKey=a.ParentKey AND n.CompanyId=a.CompanyId
          WHERE a.IsCycle=0
        )
        SELECT a.* FROM Ancestors a JOIN Nodes target ON target.NodeKey=a.TargetKey
        ORDER BY target.SortOrder, target.Code, target.NodeType, target.NodeKey, a.Depth DESC OPTION(MAXRECURSION 0);
        """;

    /// <summary>按排序码、编码、身份读取全部公司根，不引入同级分页。</summary>
    public Task<IReadOnlyList<OrganizationTreeNode>> RootsAsync(CancellationToken token) => ExecuteAsync(
        Nodes + " SELECT * FROM Nodes WHERE ParentKey IS NULL ORDER BY SortOrder, Code, NodeKey;", [],
        async reader => (IReadOnlyList<OrganizationTreeNode>)await ReadNodesAsync(reader, token), token);

    /// <summary>先确认父节点存在；空子集与不存在分别返回空列表和 null。</summary>
    public Task<IReadOnlyList<OrganizationTreeNode>?> ChildrenAsync(string nodeKey, CancellationToken token) => ExecuteAsync(
        Nodes + " SELECT COUNT(*) FROM Nodes WHERE NodeKey=@nodeKey; " + Nodes
        + " SELECT * FROM Nodes WHERE ParentKey=@nodeKey ORDER BY SortOrder, Code, NodeKey;",
        new() { ["@nodeKey"] = nodeKey }, async reader =>
        {
            await reader.ReadAsync(token);
            var exists = reader.GetInt32(0) > 0;
            await reader.NextResultAsync(token);
            var nodes = await ReadNodesAsync(reader, token);
            return exists ? (IReadOnlyList<OrganizationTreeNode>)nodes : null;
        }, token);

    /// <summary>只读取目标祖先链，包含根与目标；环或断链失败，不伪装目标不存在。</summary>
    public Task<IReadOnlyList<OrganizationTreeNode>?> PathAsync(string nodeKey, CancellationToken token) => ExecuteAsync(
        Nodes + ", Seeds AS (SELECT NodeKey FROM Nodes WHERE NodeKey=@nodeKey) " + PathProjection,
        new() { ["@nodeKey"] = nodeKey }, async reader =>
        {
            var paths = await ReadPathsAsync(reader, token);
            return paths.GetValueOrDefault(nodeKey);
        }, token);

    /// <summary>完整数据中先稳定分页，再以一次递归查询批量解析该页路径。</summary>
    public Task<OrganizationTreeSearchPage> SearchAsync(string keyword, int page, int pageSize, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(keyword) || page < 1 || pageSize < 1 || page > int.MaxValue / pageSize)
            throw new Microsoft.AspNetCore.Http.BadHttpRequestException("搜索需要非空 keyword、正页码和 正整数 pageSize。");
        const string predicate = " WHERE CHARINDEX(@keyword,DisplayName)>0 OR CHARINDEX(@keyword,Code)>0 ";
        return ExecuteAsync(Nodes + " SELECT COUNT(*) FROM Nodes " + predicate + "; " + Nodes
            + ", Seeds AS (SELECT NodeKey FROM Nodes " + predicate
            + " ORDER BY SortOrder, Code, NodeKey OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY) " + PathProjection,
            new() { ["@keyword"] = keyword.Trim(), ["@offset"] = (page-1)*pageSize, ["@take"] = pageSize }, async reader =>
            {
                await reader.ReadAsync(token);
                var total = reader.GetInt32(0);
                await reader.NextResultAsync(token);
                var paths = await ReadPathsAsync(reader, token);
                var items = paths.Values.Select(path => new OrganizationTreeSearchItem(path[^1], path)).ToArray();
                return new OrganizationTreeSearchPage(items, total, page, pageSize);
            }, token);
    }

    // 读取结果只保留所选页面或分支，不预载全树。
    private static async Task<List<OrganizationTreeNode>> ReadNodesAsync(DbDataReader reader, CancellationToken token)
    {
        var nodes = new List<OrganizationTreeNode>();
        while (await reader.ReadAsync(token)) nodes.Add(ReadNode(reader));
        return nodes;
    }

    // 每个目标的路径由数据库倒序深度返回；首节点不是根即表示持久化关系已断裂。
    private static async Task<Dictionary<string, IReadOnlyList<OrganizationTreeNode>>> ReadPathsAsync(DbDataReader reader, CancellationToken token)
    {
        var paths = new Dictionary<string, List<OrganizationTreeNode>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token))
        {
            if (reader.GetBoolean(reader.GetOrdinal("IsCycle"))) throw new DepartmentHierarchyCorruptedException();
            var target = reader.GetString(reader.GetOrdinal("TargetKey"));
            if (!paths.TryGetValue(target, out var path))
            {
                if (!reader.IsDBNull(reader.GetOrdinal("ParentKey"))) throw new DepartmentHierarchyCorruptedException();
                paths[target] = path = [];
            }
            path.Add(ReadNode(reader));
        }
        return paths.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<OrganizationTreeNode>)pair.Value, StringComparer.Ordinal);
    }

    // 业务字段仅由模块解释；公共宿主收到不透明 context。
    private static OrganizationTreeNode ReadNode(DbDataReader reader) => new(
        reader.GetString(reader.GetOrdinal("NodeKey")), reader.GetString(reader.GetOrdinal("NodeType")),
        reader.GetString(reader.GetOrdinal("DisplayName")), reader.GetBoolean(reader.GetOrdinal("HasChildren")),
        reader.GetString(reader.GetOrdinal("Code")), reader.GetBoolean(reader.GetOrdinal("IsEnabled")),
        new Dictionary<string, object?> {
            ["id"] = reader.GetString(reader.GetOrdinal("Id")),
            ["companyId"] = reader.GetString(reader.GetOrdinal("CompanyId")),
            ["parentDepartmentId"] = reader.IsDBNull(reader.GetOrdinal("ParentDepartmentId")) ? null : reader.GetString(reader.GetOrdinal("ParentDepartmentId"))
        });

    // 参数与 SQL 分离，沿用请求事务与取消，不改变连接所有权。
    private async Task<T> ExecuteAsync<T>(string sql, Dictionary<string, object?> parameters,
        Func<DbDataReader, Task<T>> read, CancellationToken token)
    {
        var connection = dbContext.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(token);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var pair in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = pair.Key; parameter.Value = pair.Value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            await using var reader = await command.ExecuteReaderAsync(token);
            return await read(reader);
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
