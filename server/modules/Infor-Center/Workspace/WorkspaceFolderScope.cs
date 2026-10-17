using System.Data.Common;
using System.Linq.Expressions;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceFolderCandidate(string Id, string Name);
public sealed record WorkspaceFolderCandidates(IReadOnlyList<WorkspaceFolderCandidate> Items, int Total, int Page, int PageSize);

/// <summary>独立详情、候选和 Link 端点共用来源或可读根路径范围；集合始终留在数据库命令内。</summary>
public sealed class WorkspaceFolderScope(EfPlatformDbContext db, IDataScopeReadPlanProvider reads, IHttpContextAccessor context)
    : ILinkQueryEndpointConstraint, IDataScopeResourceReadConstraint
{
    public string Key => "workspace.folder.scope";
    public string ModelKey => WorkspacePositions.FolderModel;
    public string ResourceKey => "workspace.folder";
    public string ModuleId => new WorkspaceModule().ModuleId;

    /// <inheritdoc/>
    public async Task<bool> IsReadableAsync(string objectId, CancellationToken token = default)
    {
        // 根参与空间遍历，但不因此成为普通对象授权/查询目标；不改变底层可达集合。
        if (await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AsNoTracking()
            .AnyAsync(space => EF.Property<string>(space, "RootFolderId") == objectId, token)) return false;
        return await ContainsAsync(context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new LinkQueryException(403, "Workspace.SubjectRequired"), objectId, token);
    }

    /// <inheritdoc/>
    public Task<Expression<Func<Dictionary<string, object?>, bool>>> ReadPredicateAsync(LinkQueryEndpoint endpoint, CancellationToken token) =>
        Task.FromResult<Expression<Func<Dictionary<string, object?>, bool>>>(row => true);

    /// <inheritdoc/>
    public async Task<LinkQuerySqlScope?> ReadSqlScopeAsync(LinkQueryEndpoint endpoint, string scopeName, CancellationToken token) =>
        await BuildAsync(context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new LinkQueryException(403, "Workspace.SubjectRequired"), scopeName, token);

    /// <summary>仅普通边扩张空间范围；中性边复用既有对象资格，visited/frontier 不枚举共享路径。</summary>
    public async Task<LinkQuerySqlScope> BuildAsync(string actorId, string name, CancellationToken token)
    {
        if (name.Length == 0 || name.Any(character => !char.IsAsciiLetterOrDigit(character))) throw new ArgumentException("Invalid trusted scope name.", nameof(name));
        var parameters = new List<DbParameter>();
        var folders = db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel).AsNoTracking();
        var plan = await reads.GetReadPlanAsync("workspace.folder", token);
        if (plan.Predicate is not null) folders = folders.Where(plan.Predicate);
        var readableSql = QuerySql(folders.Select(row => new { EID = EF.Property<string>(row, "EID"), OriginSpaceId = EF.Property<string>(row, "OriginSpaceId") }), name, parameters);
        var admittedSql = QuerySql(WorkspaceSpaces.Admitted(db, actorId).Select(row => new { EID = EF.Property<string>(row, "EID"), RootFolderId = EF.Property<string>(row, "RootFolderId") }), name, parameters);
        var readable = "@" + name + "Readable"; var admitted = "@" + name + "Admitted";
        var reached = "@" + name + "Reached"; var frontier = "@" + name + "Frontier"; var next = "@" + name + "Next";
        var link = db.Model.FindEntityType(WorkspacePositions.ContentModel)!;
        var idType = db.Model.FindEntityType(WorkspacePositions.FolderModel)!.FindProperty("EID")!.GetColumnType()!;
        var preparation = $"""
            SET NOCOUNT ON;
            DECLARE {readable} TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY, OriginSpaceId {idType} COLLATE DATABASE_DEFAULT NOT NULL);
            INSERT INTO {readable}(EID,OriginSpaceId) {readableSql};
            DECLARE {admitted} TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY, RootFolderId {idType} COLLATE DATABASE_DEFAULT NOT NULL);
            INSERT INTO {admitted}(EID,RootFolderId) {admittedSql};
            DECLARE {reached} TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            DECLARE {frontier} TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            DECLARE {next} TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            INSERT INTO {reached} SELECT r.EID FROM {readable} r JOIN {admitted} s ON s.RootFolderId=r.EID AND s.EID=r.OriginSpaceId;
            INSERT INTO {frontier} SELECT EID FROM {reached};
            WHILE EXISTS(SELECT 1 FROM {frontier})
            BEGIN
                INSERT INTO {next} SELECT DISTINCT l.{Column(link, "CEID")} FROM {Table(link)} l
                    JOIN {frontier} f ON f.EID=l.{Column(link, "PEID")}
                    JOIN {readable} r ON r.EID=l.{Column(link, "CEID")}
                    WHERE l.{Column(link, "ChildModelKey")}=N'WorkspaceFolder'
                    AND (l.{Column(link, "IsScopeNeutral")} IS NULL OR l.{Column(link, "IsScopeNeutral")}=0)
                    AND NOT EXISTS(SELECT 1 FROM {reached} v WHERE v.EID=r.EID);
                DELETE FROM {frontier};
                INSERT INTO {frontier} SELECT EID FROM {next};
                INSERT INTO {reached} SELECT EID FROM {next};
                DELETE FROM {next};
            END;
            """;
        return new(preparation, $"SELECT EID FROM {reached} UNION SELECT r.EID FROM {readable} r JOIN {admitted} s ON s.EID=r.OriginSpaceId", parameters);
    }

    /// <summary>裸对象读取只返回存在性；不会把可达集合或私人来源返回应用进程。</summary>
    public async Task<bool> ContainsAsync(string actorId, string id, CancellationToken token)
    {
        var scope = await BuildAsync(actorId, "folderRead", token);
        var parameters = scope.Parameters.ToList(); parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@folderTarget", id));
        return await ExecuteAsync(scope.Preparation + "SELECT 1 WHERE @folderTarget IN (" + scope.IdsQuery + ");", parameters, async reader => await reader.ReadAsync(token), token);
    }

    /// <summary>单选候选按 EID 去重并排除全部正式根；范围、关键词、计数和分页在 SQL 内完成。</summary>
    public async Task<WorkspaceFolderCandidates> CandidatesAsync(string actorId, int page, int size, string? keyword, CancellationToken token)
    {
        page = page <= 0 ? 1 : page; size = size <= 0 ? 20 : Math.Min(size, 100);
        if (page > int.MaxValue / size) throw new LinkQueryException(400, "Workspace.InvalidPage", "pageIndex");
        var scope = await BuildAsync(actorId, "folderCandidates", token);
        var parameters = scope.Parameters.ToList();
        var rows = db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel).AsNoTracking();
        var spaces = db.Set<Dictionary<string, object?>>("WorkspaceSpace").AsNoTracking();
        rows = rows.Where(row => !spaces.Any(space => EF.Property<string>(space, "RootFolderId") == EF.Property<string>(row, "EID")));
        if (!string.IsNullOrWhiteSpace(keyword)) rows = rows.Where(row => EF.Property<string>(row, "ENAME").Contains(keyword));
        var candidates = QuerySql(rows.Select(row => new { EID = EF.Property<string>(row, "EID"), ENAME = EF.Property<string>(row, "ENAME") }), "candidateFilter", parameters);
        var query = "WITH Candidates AS (SELECT c.* FROM (" + candidates + ") c WHERE c.EID IN (" + scope.IdsQuery + ")) ";
        parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@offset", (page - 1) * size)); parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@size", size));
        return await ExecuteAsync(scope.Preparation + query + "SELECT COUNT(*) FROM Candidates; " + query + "SELECT EID,ENAME FROM Candidates ORDER BY ENAME,EID OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;", parameters, async reader =>
        {
            await reader.ReadAsync(token); var total = reader.GetInt32(0); await reader.NextResultAsync(token);
            var items = new List<WorkspaceFolderCandidate>();
            while (await reader.ReadAsync(token)) items.Add(new(reader.GetString(0), reader.GetString(1)));
            return new WorkspaceFolderCandidates(items, total, page, size);
        }, token);
    }

    // EF 的可信物理映射与参数类型保留；不同 scope 前缀隔离复合命令中的名称。
    internal static string QuerySql<T>(IQueryable<T> rows, string prefix, List<DbParameter> parameters)
    {
        using var command = rows.CreateDbCommand(); var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DbParameter parameter in command.Parameters)
        {
            var name = "@" + prefix + "Parameter" + parameters.Count;
            names.Add(parameter.ParameterName, name); var copy = (DbParameter)((ICloneable)parameter).Clone(); copy.ParameterName = name; parameters.Add(copy);
        }
        return LinkQuerySqlScope.RenameParameters(command.CommandText.Trim().TrimEnd(';'), names);
    }

    // 命令内表变量随成功、取消和异常一并结束，不建立连接级或跨主体缓存。
    internal async Task<T> ExecuteAsync<T>(string sql, IReadOnlyList<DbParameter> parameters, Func<DbDataReader, Task<T>> consume, CancellationToken token)
    {
        var connection = db.Database.GetDbConnection(); var close = connection.State != System.Data.ConnectionState.Open;
        if (close) await connection.OpenAsync(token);
        try
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            // 范围可在同一操作内执行多条命令；参数副本归当前 command，原范围保留 provider 类型和值。
            foreach (var parameter in parameters) command.Parameters.Add((DbParameter)((ICloneable)parameter).Clone());
            await using var reader = await command.ExecuteReaderAsync(token); return await consume(reader);
        }
        finally { if (close) await connection.CloseAsync(); }
    }

    // SQL 标识符只读取正式 EF 模型，并保留其 provider 映射。
    internal static string Table(IEntityType entity) => Quote(entity.GetSchema() ?? "dbo") + "." + Quote(entity.GetTableName()!);
    // 列映射不是请求字段目录，客户端不能传入物理列名。
    internal static string Column(IEntityType entity, string field) => Quote(entity.FindProperty(field)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    // 可信标识仍转义闭合符，值使用独立数据库参数。
    private static string Quote(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
