using System.Data;
using System.Text.Json;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Runtime;

public sealed partial class LinkTreeReader
{
    /// <summary>授权后 DAG 先序计数；数据库只为当前页构造完整出现链，空词不查询图。</summary>
    public async Task<LinkTreeSearchPage> SearchAsync(LinkTreeDefinition definition, string? keyword, int page, int size, CancellationToken token)
    {
        keyword = keyword?.Trim() ?? "";
        if (keyword.Length > 256) throw new LinkQueryException(400, "Platform.TreeInvalidKeyword", "keyword");
        if (page < 1 || size is < 1 or > 100) throw new LinkQueryException(400, "Platform.TreeInvalidPage");
        token.ThrowIfCancellationRequested();
        var readSource = EntryReadSource(definition);
        if (keyword.Length == 0)
        {
            linkReads.ValidateEntrySource(definition.Source.ResourceKey, definition.Source.ModelKey, readSource);
            return new([], 0, page, size);
        }
        var plan = Compile(definition);
        var rootSql = await plan.Policy.BuildAsync(definition, token);
        var scope = await linkReads.ReadAsync(definition.Source.ResourceKey, definition.Source.ModelKey, "treeSearch", readSource, token);
        var source = await sourceReads.TreeSqlAsync(plan.Link, "treeSearchSource", token);
        var parameters = scope.Parameters.Concat(rootSql.Parameters).Concat(source.Parameters).ToList();
        var links = plan.LinkEntity; var folder = plan.NodeEntity;
        var idType = folder.FindProperty(plan.Endpoint.IdField)!.GetColumnType()!;
        var linkType = links.FindProperty("LID")!.GetColumnType()!;
        var table = Table(links);
        var folderSql = $"SELECT n.{Column(folder, plan.Endpoint.IdField)} AS EID,n.{Column(folder, plan.LabelField)} AS ENAME FROM {Table(folder)} n WHERE n.{Column(folder, plan.Endpoint.IdField)} IN ({scope.IdsQuery})";
        var rootsQuery = "SELECT RootKey AS RootKey,RootObjectId AS RootId,Label AS RootName,LabelKey,RootOrder FROM (" + rootSql.RootsQuery + ") roots";
        var allowedTypes = string.Join(",", plan.Link.Model.LinkedTypes.Where(pair => pair.Parent == definition.Source.ModelKey)
            .Select(pair => "N'" + pair.Child.Replace("'", "''", StringComparison.Ordinal) + "'"));
        var sql = scope.Preparation + rootSql.Preparation + source.Preparation + $"""
            /* Platform Link occurrence search */
            SET NOCOUNT ON;
            SELECT f.*, CONVERT(bigint,CASE WHEN CHARINDEX(@searchKeyword COLLATE Latin1_General_100_CI_AS_SC,f.ENAME COLLATE Latin1_General_100_CI_AS_SC)>0 THEN 1 ELSE 0 END) AS Hit,
                CONVERT(bigint,0) AS Hits, CONVERT(int,0) AS Remaining, CONVERT(bit,0) AS Done
                INTO #LinkTreeSearchNodes FROM ({folderSql}) f;
            CREATE UNIQUE CLUSTERED INDEX IX_LinkTreeSearchNodes ON #LinkTreeSearchNodes(EID);
            SELECT s.* INTO #LinkTreeSearchRoots FROM ({rootsQuery}) s;
            DELETE r FROM #LinkTreeSearchRoots r WHERE NOT EXISTS(SELECT 1 FROM #LinkTreeSearchNodes n WHERE n.EID=r.RootId);
            SELECT l.{Column(links, "LID")} AS LID, l.{Column(links, plan.Link.Metadata.Parent.ReferenceField)} AS ParentId,
                l.{Column(links, plan.Link.Metadata.Child.ReferenceField)} AS ChildId,l.{Column(links, definition.Source.OrderField)} AS Ordinal
                INTO #LinkTreeSearchEdges FROM {table} l
                JOIN #LinkTreeSearchNodes p ON p.EID=l.{Column(links, plan.Link.Metadata.Parent.ReferenceField)}
                JOIN #LinkTreeSearchNodes c ON c.EID=l.{Column(links, plan.Link.Metadata.Child.ReferenceField)}
                WHERE l.{Column(links, "LID")} IN ({source.IdsQuery}) AND {EndType(plan, "l", false)}=@searchModel AND {EndType(plan, "l", true)}=@searchModel;
            CREATE UNIQUE CLUSTERED INDEX IX_LinkTreeSearchEdges ON #LinkTreeSearchEdges(ParentId,LID);
            CREATE INDEX IX_LinkTreeSearchEdgesChild ON #LinkTreeSearchEdges(ChildId);
            CREATE TABLE #LinkTreeSearchReach(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            CREATE TABLE #LinkTreeSearchFront(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            CREATE TABLE #LinkTreeSearchNext(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            INSERT #LinkTreeSearchReach SELECT DISTINCT RootId FROM #LinkTreeSearchRoots;
            INSERT #LinkTreeSearchFront SELECT EID FROM #LinkTreeSearchReach;
            WHILE EXISTS(SELECT 1 FROM #LinkTreeSearchFront)
            BEGIN
                INSERT #LinkTreeSearchNext SELECT DISTINCT e.ChildId FROM #LinkTreeSearchEdges e JOIN #LinkTreeSearchFront f ON f.EID=e.ParentId
                    WHERE NOT EXISTS(SELECT 1 FROM #LinkTreeSearchReach r WHERE r.EID=e.ChildId);
                DELETE FROM #LinkTreeSearchFront;
                INSERT #LinkTreeSearchFront SELECT EID FROM #LinkTreeSearchNext;
                INSERT #LinkTreeSearchReach SELECT EID FROM #LinkTreeSearchNext;
                DELETE FROM #LinkTreeSearchNext;
            END;
            DELETE e FROM #LinkTreeSearchEdges e WHERE NOT EXISTS(SELECT 1 FROM #LinkTreeSearchReach r WHERE r.EID=e.ParentId);
            DELETE n FROM #LinkTreeSearchNodes n WHERE NOT EXISTS(SELECT 1 FROM #LinkTreeSearchReach r WHERE r.EID=n.EID);
            IF EXISTS(SELECT 1 FROM #LinkTreeSearchEdges WHERE Ordinal IS NULL) THROW 51001,'Platform.TreeGraphInvalid',1;
            IF EXISTS(SELECT 1 FROM {table} l JOIN #LinkTreeSearchReach r ON r.EID=l.{Column(links, plan.Link.Metadata.Parent.ReferenceField)}
                WHERE l.{Column(links, "LID")} IN ({source.IdsQuery}) AND {EndType(plan, "l", true)}=@searchModel AND ({EndType(plan, "l", false)} NOT IN ({allowedTypes})
                OR {EndType(plan, "l", false)}=@searchModel AND NOT EXISTS(SELECT 1 FROM {Table(folder)} f
                    WHERE f.{Column(folder, plan.Endpoint.IdField)}=l.{Column(links, plan.Link.Metadata.Child.ReferenceField)})))
                THROW 51001,'Platform.TreeGraphInvalid',1;
            UPDATE n SET Remaining=(SELECT COUNT(*) FROM #LinkTreeSearchEdges e WHERE e.ParentId=n.EID) FROM #LinkTreeSearchNodes n;
            -- 汇聚节点只计算一次子树权重；每条入向边在父权重中各贡献一次，完整路径不会被去重。
            WHILE EXISTS(SELECT 1 FROM #LinkTreeSearchNodes WHERE Done=0)
            BEGIN
                INSERT #LinkTreeSearchFront SELECT EID FROM #LinkTreeSearchNodes WHERE Done=0 AND Remaining=0;
                IF NOT EXISTS(SELECT 1 FROM #LinkTreeSearchFront) THROW 51001,'Platform.TreeGraphInvalid',1;
                UPDATE n SET Hits=n.Hit+COALESCE((SELECT SUM(c.Hits) FROM #LinkTreeSearchEdges e
                    JOIN #LinkTreeSearchNodes c ON c.EID=e.ChildId WHERE e.ParentId=n.EID),0),Done=1
                    FROM #LinkTreeSearchNodes n JOIN #LinkTreeSearchFront f ON f.EID=n.EID;
                UPDATE n SET Remaining=n.Remaining-d.Amount FROM #LinkTreeSearchNodes n JOIN
                    (SELECT e.ParentId,COUNT(*) Amount FROM #LinkTreeSearchEdges e JOIN #LinkTreeSearchFront f ON f.EID=e.ChildId GROUP BY e.ParentId) d ON d.ParentId=n.EID;
                DELETE FROM #LinkTreeSearchFront;
            END;
            -- 只从根出现的权重扣除自身命中；同一实体在其他根的普通出现仍保留命中。
            SELECT r.*,n.Hit AS RootHit,SUM(n.Hits-n.Hit) OVER(ORDER BY r.RootOrder,r.RootKey,r.RootId ROWS UNBOUNDED PRECEDING)-(n.Hits-n.Hit) AS FirstHit,
                SUM(n.Hits-n.Hit) OVER(ORDER BY r.RootOrder,r.RootKey,r.RootId ROWS UNBOUNDED PRECEDING) AS EndHit
                INTO #LinkTreeSearchRootRanges FROM #LinkTreeSearchRoots r JOIN #LinkTreeSearchNodes n ON n.EID=r.RootId;
            SELECT e.*,SUM(n.Hits) OVER(PARTITION BY e.ParentId ORDER BY e.Ordinal,e.LID ROWS UNBOUNDED PRECEDING)-n.Hits AS FirstHit,
                SUM(n.Hits) OVER(PARTITION BY e.ParentId ORDER BY e.Ordinal,e.LID ROWS UNBOUNDED PRECEDING) AS EndHit
                INTO #LinkTreeSearchRanges FROM #LinkTreeSearchEdges e JOIN #LinkTreeSearchNodes n ON n.EID=e.ChildId WHERE n.Hits>0;
            CREATE INDEX IX_LinkTreeSearchRanges ON #LinkTreeSearchRanges(ParentId,EndHit);
            DECLARE @total bigint=COALESCE((SELECT MAX(EndHit) FROM #LinkTreeSearchRootRanges),0);
            -- JSON number/SDK number 必须精确；表达范围用尽是未完成，绝不返回近似 total。
            IF @total>9007199254740991 THROW 51002,'Platform.TreeSearchIncomplete',1;
            CREATE TABLE #LinkTreeSearchResults(Sequence int,RootKey nvarchar(max) COLLATE DATABASE_DEFAULT,RootId {idType} COLLATE DATABASE_DEFAULT,
                LabelKey nvarchar(max),FolderId {idType} COLLATE DATABASE_DEFAULT,Name nvarchar(max),LinkIds nvarchar(max),Names nvarchar(max),HasChildren bit);
            DECLARE @sequence int=0,@absolute bigint=@searchOffset,@rank bigint,@space nvarchar(max),@root {idType},@current {idType},
                @labelKey nvarchar(max),@rootName nvarchar(max),@name nvarchar(max),@lid {linkType},@first bigint,@hit bigint,@linkIds nvarchar(max),@names nvarchar(max);
            WHILE @sequence<@searchSize AND @absolute<@total
            BEGIN
                SELECT TOP(1) @space=RootKey,@root=RootId,@current=RootId,@labelKey=LabelKey,@rootName=RootName,@rank=@absolute-FirstHit+RootHit
                    FROM #LinkTreeSearchRootRanges WHERE FirstHit<=@absolute AND EndHit>@absolute ORDER BY EndHit;
                SET @linkIds=N'[]'; SET @names=JSON_MODIFY(N'[]','append $',@rootName);
                WHILE 1=1
                BEGIN
                    SELECT @hit=Hit,@name=ENAME FROM #LinkTreeSearchNodes WHERE EID=@current;
                    IF @hit=1 AND @rank=0 BREAK;
                    SET @rank=@rank-@hit;
                    SELECT TOP(1) @lid=LID,@current=ChildId,@first=FirstHit FROM #LinkTreeSearchRanges
                        WHERE ParentId=@current AND EndHit>@rank ORDER BY EndHit;
                    IF @@ROWCOUNT=0 THROW 51001,'Platform.TreeGraphInvalid',1;
                    SET @rank=@rank-@first;
                    SET @linkIds=JSON_MODIFY(@linkIds,'append $',@lid);
                    SELECT @name=ENAME FROM #LinkTreeSearchNodes WHERE EID=@current;
                    SET @names=JSON_MODIFY(@names,'append $',@name);
                END;
                INSERT #LinkTreeSearchResults VALUES(@sequence,@space,@root,@labelKey,@current,@name,@linkIds,@names,
                    CASE WHEN EXISTS(SELECT 1 FROM #LinkTreeSearchEdges WHERE ParentId=@current) THEN 1 ELSE 0 END);
                SET @sequence=@sequence+1; SET @absolute=@absolute+1;
            END;
            SELECT @total;
            SELECT RootKey,RootId,LabelKey,FolderId,Name,LinkIds,Names,HasChildren FROM #LinkTreeSearchResults ORDER BY Sequence;
            DROP TABLE #LinkTreeSearchResults,#LinkTreeSearchRanges,#LinkTreeSearchRootRanges,#LinkTreeSearchNext,#LinkTreeSearchFront,#LinkTreeSearchReach,#LinkTreeSearchEdges,#LinkTreeSearchRoots,#LinkTreeSearchNodes;
            """;
        // 查询使用补充字符感知的 Unicode 包含比较，不修改名称存储或树次序。
        parameters.Add(new SqlParameter("@searchKeyword", SqlDbType.NVarChar, 256) { Value = keyword });
        parameters.Add(new SqlParameter("@searchModel", definition.Source.ModelKey));
        parameters.Add(new SqlParameter("@searchOffset", SqlDbType.BigInt) { Value = ((long)page - 1) * size });
        parameters.Add(new SqlParameter("@searchSize", size));
        // 根、授权图、计数及页项保持同一事务可见性；不在分页后过滤。
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var result = await ExecuteAsync(sql, parameters, async reader =>
        {
            await reader.ReadAsync(token); var total = reader.GetInt64(0);
            await reader.NextResultAsync(token);
            var items = new List<LinkTreeSearchItem>(size);
            while (await reader.ReadAsync(token))
            {
                var position = new LinkTreePosition(reader.GetString(0), reader.GetString(1), JsonSerializer.Deserialize<string[]>(reader.GetString(5))!);
                var names = JsonSerializer.Deserialize<string[]>(reader.GetString(6))!;
                var root = new Root(position.RootKey, position.RootObjectId, names[0], reader.IsDBNull(2) ? null : reader.GetString(2));
                var node = Node(plan, root, reader.GetString(3), reader.GetString(4), position, reader.GetBoolean(7), false);
                var path = names.Select((label, index) => new LinkTreePathItem(LinkTreeNodeKey.Encode(position with { LinkIds = position.LinkIds.Take(index).ToArray() }), label, index == 0 ? root.LabelKey : null)).ToArray();
                items.Add(new(node, path));
            }
            while (await reader.NextResultAsync(token)) { }
            return new LinkTreeSearchPage(items, total, page, size);
        }, token);
        await transaction.CommitAsync(token);
        return result;
    }
}
