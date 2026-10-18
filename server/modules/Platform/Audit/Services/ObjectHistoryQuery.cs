using System.Linq.Expressions;
using System.Net;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Presentation;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Audit.Services;

/// <summary>普通查询独立于管理员查询；始终完整读取，再按精确策略在SQL中过滤分页。</summary>
public sealed partial class ObjectHistoryQuery(EfPlatformDbContext db, IDataScopeObjectAccess access,
    IDataScopeResourceCatalog resources,
    IEnumerable<ObjectHistoryEventPolicy> policies, IUserDisplayNameDirectory userNames,
    TimeProvider clock, IDataScopeEvaluationScope evaluation, IRuntimeTextCatalog texts, IRequestPresentationContextAccessor presentation,
    IDataScopeReadableIds readableIds, ICP.Platform.Crud.Abstractions.IStandardCrudResourceRegistry crudResources,
    IEnumerable<IBusinessAuditNameDirectory> objectNames) : IObjectHistoryQuery
{
    /// <inheritdoc/>
    public Task<ObjectHistoryPage?> ListAsync(string resourceKey, string objectId, ObjectHistoryFilter filter,
        CancellationToken cancellationToken = default) => ReadAsync(resourceKey, objectId, filter, null, cancellationToken);

    /// <inheritdoc/>
    public async Task<ObjectHistoryItem?> GetAsync(string resourceKey, string objectId, string recordId, ObjectHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        var page = await ReadAsync(resourceKey, objectId, filter, recordId, cancellationToken);
        return page?.Items.SingleOrDefault();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ObjectHistoryItem>?> OutputAsync(string resourceKey, string objectId, ObjectHistoryOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Filter is null || request.Mode is not ("selected" or "all")
            || request.Mode == "all" && request.SelectedIds is { Count: > 0 }
            || request.Mode == "selected" && (request.SelectedIds is not { Count: > 0 } || request.SelectedIds.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("履历输出范围无效。", nameof(request));
        var ids = request.Mode == "selected" ? request.SelectedIds!.Distinct(StringComparer.Ordinal).ToArray() : null;
        var page = await ReadAsync(resourceKey, objectId, request.Filter with { PageIndex = 1, PageSize = int.MaxValue }, null,
            cancellationToken, ids);
        return page?.Items;
    }

    // 列表与详情共用资格、精确关联、筛选及安全投影；详情的记录键在SQL中相交，而不是跨对象按ID直取。
    private async Task<ObjectHistoryPage?> ReadAsync(string resourceKey, string objectId, ObjectHistoryFilter filter, string? recordId,
        CancellationToken cancellationToken, IReadOnlyCollection<string>? selectedIds = null)
    {
        // 权限、记录和安全名称属于同一串行只读查询；不反复打开连接，也不改变调用方已持有的连接。
        var openedHere = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            using var operation = evaluation.BeginOperation();
            if (!await access.IsReadableAsync(resourceKey, objectId, cancellationToken)) return null;
            if (filter.PageSize <= 0 || filter.PageIndex <= 0 || filter.PageIndex > int.MaxValue / filter.PageSize
                || filter.From is { Kind: not DateTimeKind.Utc } || filter.To is { Kind: not DateTimeKind.Utc }
                || filter.From > filter.To) throw new ArgumentException("普通履历时间或分页输入无效。", nameof(filter));
            string? ipAddress = null;
            if (filter.IpAddress is not null)
            {
                if (!IPAddress.TryParse(filter.IpAddress, out var address)) throw new ArgumentException("履历IP筛选无效。", nameof(filter));
                ipAddress = address.ToString();
            }
            var model = resources.Resources.Single(resource => resource.ResourceKey == resourceKey).ModelName;
            var from = filter.From ?? clock.GetUtcNow().UtcDateTime.AddMonths(-3);
            var selected = policies.Where(policy => policy.TargetResourceKey is null || policy.TargetResourceKey == resourceKey)
                .Where(policy => string.IsNullOrWhiteSpace(filter.Description)
                    || texts.GetText(policy.DescriptionKey, presentation.Current?.Locale).Contains(filter.Description, StringComparison.OrdinalIgnoreCase)).ToArray();
            var predicate = Matches(selected, resourceKey, objectId, model);
            var query = db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().Where(predicate)
                .Where(row => EF.Property<DateTime>(row, "OccurredAt") >= from);
            if (selectedIds is not null) query = query.Where(row => EF.Parameter(selectedIds).Contains(EF.Property<string>(row, "EID")));
            if (ipAddress is not null) query = query.Where(row => EF.Property<string?>(row, "IpAddress") == ipAddress);
            if (recordId is not null) query = query.Where(row => EF.Property<string>(row, "EID") == recordId);
            if (filter.To is { } to) query = query.Where(row => EF.Property<DateTime>(row, "OccurredAt") <= to);
            if (!string.IsNullOrWhiteSpace(filter.ActorId)) query = query.Where(row => EF.Property<string>(row, "ActorId") == filter.ActorId);
            if (!string.IsNullOrWhiteSpace(filter.EventType)) query = query.Where(row => EF.Property<string>(row, "EventType") == filter.EventType);
            // 所有类型关联与筛选保持在SQL；Folder中的Part关系事件仍然是Part事件，不伪装成Folder类型。
            var typed = query.Select(row => new HistoryRow { Id = EF.Property<string>(row, "EID"), EventType = EF.Property<string>(row, "EventType"),
                ActorId = EF.Property<string>(row, "ActorId"), OccurredAt = EF.Property<DateTime>(row, "OccurredAt"), IpAddress = EF.Property<string?>(row, "IpAddress"), ObjectType = model,
                Resource = EF.Property<string>(row, "Resource"), TargetId = EF.Property<string>(row, "TargetId") });
            var typePolicies = selected.Where(policy => policy.ObjectTypeField is not null)
                .DistinctBy(policy => (policy.EventType, policy.RecordResource, policy.ObjectTypeField)).ToArray();
            if (typePolicies.Length > 0)
            {
                IQueryable<StoredObjectType>? types = null;
                foreach (var policy in typePolicies) types = types is null ? TypeRows(policy, resourceKey) : types.Concat(TypeRows(policy, resourceKey));
                var typeRows = types!.Distinct();
                var typedEvents = typePolicies.Select(policy => policy.EventType).Distinct(StringComparer.Ordinal).ToArray();
                var knownModels = resources.Resources.Select(resource => resource.ModelName).Distinct(StringComparer.Ordinal).ToArray();
                typed = typed.Where(row => !typedEvents.Contains(row.EventType)
                        || typeRows.Any(type => type.Id == row.Id && knownModels.Contains(type.ObjectType)))
                    .Select(row => new HistoryRow { Id = row.Id, EventType = row.EventType, ActorId = row.ActorId,
                        OccurredAt = row.OccurredAt, IpAddress = row.IpAddress, Resource = row.Resource, TargetId = row.TargetId,
                        ObjectType = typeRows.Where(type => type.Id == row.Id).Select(type => type.ObjectType).FirstOrDefault() ?? row.ObjectType });
            }
            if (!string.IsNullOrWhiteSpace(filter.ObjectType)) typed = typed.Where(row => row.ObjectType == filter.ObjectType);
            var total = await typed.CountAsync(cancellationToken);
            if (selectedIds is not null && total != selectedIds.Count) return null;
            // 仅读取白名单列，连数据库到应用的普通读取也不搬运ACL原文/Trace或任意字段JSON。
            var rows = await typed.OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id)
                .Skip(recordId is null ? (filter.PageIndex - 1) * filter.PageSize : 0).Take(recordId is null ? filter.PageSize : 1).ToArrayAsync(cancellationToken);
            var targets = await ReadTargetsAsync(rows, selected, cancellationToken);
            var names = await userNames.FindManyAsync(rows.Select(row => row.ActorId).Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);
            var items = rows.Select(row =>
            {
                var name = names.GetValueOrDefault(row.ActorId);
                var available = !string.IsNullOrWhiteSpace(name) && name != "***";
                var description = selected.Where(policy => policy.EventType == row.EventType).Select(policy => policy.DescriptionKey).Distinct(StringComparer.Ordinal).Single();
                return new ObjectHistoryItem(row.Id, row.EventType, DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc), row.ObjectType,
                    new(available ? name : null, available ? "available" : "unavailable"), targets[row.Id],
                    new(description, new Dictionary<string, string>()), row.IpAddress);
            }).ToArray();
            return new(items, total, filter.PageIndex, filter.PageSize);
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }

    // 只组合可信owner登记的精确事件、记录资源及目标等式，不搜索任意JSON字符串。
    private Expression<Func<Dictionary<string, object?>, bool>> Matches(ObjectHistoryEventPolicy[] selected, string resourceKey, string objectId, string modelKey)
    {
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        Expression body = Expression.Constant(false);
        foreach (var policy in selected)
        {
            if (policy.TargetFormat == ObjectHistoryTargetFormat.DeclaredFields)
            {
                if (policy.FieldAssociations is not { Count: > 0 }) throw new InvalidOperationException("履历字段关联缺少owner声明。");
                foreach (var field in policy.FieldAssociations)
                {
                    var ids = FieldMatches(policy, field, resourceKey, objectId, modelKey);
                    Expression<Func<Dictionary<string, object?>, bool>> association = value => ids.Contains(EF.Property<string>(value, "EID"));
                    body = Expression.OrElse(body, new ReplaceParameter(association.Parameters[0], row).Visit(association.Body));
                }
                continue;
            }
            if (policy.TargetFormat is not (ObjectHistoryTargetFormat.ObjectId or ObjectHistoryTargetFormat.QualifiedObjectId))
                throw new InvalidOperationException("未知履历目标格式。");
            var target = policy.TargetFormat == ObjectHistoryTargetFormat.QualifiedObjectId ? resourceKey + "/" + objectId : objectId;
            Expression<Func<Dictionary<string, object?>, bool>> match = value => EF.Property<string>(value, "EventType") == policy.EventType
                && EF.Property<string>(value, "Resource") == (policy.RecordResource ?? resourceKey)
                && EF.Property<string>(value, "TargetId") == target;
            body = Expression.OrElse(body, new ReplaceParameter(match.Parameters[0], row).Visit(match.Body));
        }
        return Expression.Lambda<Func<Dictionary<string, object?>, bool>>(body, row);
    }

    // 兼容未改旧writer的固定字段数组；只按event/resource/Field/Kind/Value精确关联，原LID删除也不改写历史。
    private IQueryable<string> FieldMatches(ObjectHistoryEventPolicy policy, ObjectHistoryFieldAssociation field,
        string resourceKey, string objectId, string modelKey)
    {
        if (string.IsNullOrWhiteSpace(field.Field) || field.Side is not (ObjectHistoryFieldSide.Before or ObjectHistoryFieldSide.After))
            throw new InvalidOperationException("非法履历字段关联声明。");
        var resource = policy.RecordResource ?? resourceKey;
        var kind = field.Kind ?? modelKey;
        var before = field.Side == ObjectHistoryFieldSide.Before;
        // OPENJSON只读取BusinessAuditField既定形状；畸形/不可证明的旧字段不进入普通数据集，仍保留管理员原记录。
        return db.Database.SqlQuery<string>($"""
            SELECT b.[EID] AS [Value]
            FROM [BusinessChange] AS b
            WHERE b.[Resource] COLLATE Latin1_General_100_BIN2 = {resource}
              AND b.[EventType] COLLATE Latin1_General_100_BIN2 = {policy.EventType}
              AND EXISTS (
                  SELECT 1
                  FROM OPENJSON(CASE WHEN ISJSON(b.[FieldsJson]) = 1 THEN b.[FieldsJson] ELSE N'[]' END)
                  WITH ([FieldName] nvarchar(128) '$.Field',
                        [BeforeKind] nvarchar(128) '$.Before.Kind', [BeforeValue] nvarchar(max) '$.Before.Value',
                        [AfterKind] nvarchar(128) '$.After.Kind', [AfterValue] nvarchar(max) '$.After.Value') AS f
                  WHERE f.[FieldName] COLLATE Latin1_General_100_BIN2 = {field.Field}
                    AND (({before} = CAST(1 AS bit)
                          AND f.[BeforeKind] COLLATE Latin1_General_100_BIN2 = {kind}
                          AND f.[BeforeValue] COLLATE Latin1_General_100_BIN2 = {objectId})
                      OR ({before} = CAST(0 AS bit)
                          AND f.[AfterKind] COLLATE Latin1_General_100_BIN2 = {kind}
                          AND f.[AfterValue] COLLATE Latin1_General_100_BIN2 = {objectId})))
            """);
    }

    // 类型只来自声明的BusinessAuditField文本事实，缺失或未知模型在外层SQL排除，不假装当前父对象类型。
    private IQueryable<StoredObjectType> TypeRows(ObjectHistoryEventPolicy policy, string resourceKey)
    {
        var field = policy.ObjectTypeField ?? throw new InvalidOperationException("缺少事件类型字段声明。");
        if (string.IsNullOrWhiteSpace(field.Field) || field.Side is not (ObjectHistoryFieldSide.Before or ObjectHistoryFieldSide.After))
            throw new InvalidOperationException("非法事件类型字段声明。");
        var resource = policy.RecordResource ?? resourceKey;
        var before = field.Side == ObjectHistoryFieldSide.Before;
        return db.Database.SqlQuery<StoredObjectType>($"""
            SELECT b.[EID] AS [Id], CASE WHEN {before} = CAST(1 AS bit) THEN f.[BeforeValue] ELSE f.[AfterValue] END AS [ObjectType]
            FROM [BusinessChange] AS b
            CROSS APPLY OPENJSON(CASE WHEN ISJSON(b.[FieldsJson]) = 1 THEN b.[FieldsJson] ELSE N'[]' END)
            WITH ([FieldName] nvarchar(128) '$.Field',
                  [BeforeKind] nvarchar(128) '$.Before.Kind', [BeforeValue] nvarchar(max) '$.Before.Value',
                  [AfterKind] nvarchar(128) '$.After.Kind', [AfterValue] nvarchar(max) '$.After.Value') AS f
            WHERE b.[Resource] COLLATE Latin1_General_100_BIN2 = {resource}
              AND b.[EventType] COLLATE Latin1_General_100_BIN2 = {policy.EventType}
              AND f.[FieldName] COLLATE Latin1_General_100_BIN2 = {field.Field}
              AND (({before} = CAST(1 AS bit) AND f.[BeforeKind] COLLATE Latin1_General_100_BIN2 = N'text' AND f.[BeforeValue] IS NOT NULL)
                OR ({before} = CAST(0 AS bit) AND f.[AfterKind] COLLATE Latin1_General_100_BIN2 = N'text' AND f.[AfterValue] IS NOT NULL))
            """);
    }

    private sealed class HistoryRow
    {
        public required string Id { get; init; }
        public required string EventType { get; init; }
        public required string ActorId { get; init; }
        public required string Resource { get; init; }
        public required string TargetId { get; init; }
        public required string ObjectType { get; init; }
        public DateTime OccurredAt { get; init; }
        public string? IpAddress { get; init; }
    }

    private sealed class StoredObjectType
    {
        public required string Id { get; init; }
        public required string ObjectType { get; init; }
    }

    // 统一表达式参数后仍由EF翻译SQL，不退回内存执行。
    private sealed class ReplaceParameter(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        /// <inheritdoc/>
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
    }
}
