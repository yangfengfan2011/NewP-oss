using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Audit.Services;

public sealed partial class ObjectHistoryQuery
{
    // SQL已确定记录集合；这里只批量解析本页主体，不因名称缺失丢记录或更改Count。
    private async Task<IReadOnlyDictionary<string, ObjectHistoryTarget>> ReadTargetsAsync(HistoryRow[] rows,
        ObjectHistoryEventPolicy[] selected, CancellationToken token)
    {
        // 显式集合保留单个SQL数组参数，避免阈值前展开数千标量参数造成编译/分配成本。
        var subjects = new Dictionary<string, (DataScopeResource Resource, string Id)>(StringComparer.Ordinal);
        foreach (var group in rows.GroupBy(row => (row.EventType, row.Resource, row.ObjectType)))
        {
            var resource = resources.Resources.SingleOrDefault(value => value.ModelName == group.Key.ObjectType);
            if (resource is null) continue;
            var policy = selected.First(value => value.EventType == group.Key.EventType
                && (value.RecordResource ?? resource.ResourceKey) == group.Key.Resource);
            var ids = group.Select(row => row.Id).ToArray();
            var facts = policy.SubjectField is not null
                ? await TargetFacts(policy, resource.ModelName, group.Key.Resource, includeText: false).Where(row => EF.Parameter(ids).Contains(row.Id)).ToDictionaryAsync(row => row.Id, token)
                : new Dictionary<string, StoredTargetFacts>(StringComparer.Ordinal);
            foreach (var row in group)
            {
                var fact = facts.GetValueOrDefault(row.Id);
                var id = policy.SubjectField is not null ? fact?.SubjectId
                    : policy.TargetFormat == ObjectHistoryTargetFormat.ObjectId ? row.TargetId
                    : policy.TargetFormat == ObjectHistoryTargetFormat.QualifiedObjectId
                        && row.TargetId.StartsWith(resource.ResourceKey + "/", StringComparison.Ordinal)
                        ? row.TargetId[(resource.ResourceKey.Length + 1)..] : null;
                if (!string.IsNullOrWhiteSpace(id)) subjects[row.Id] = (resource, id);
            }
        }
        var result = rows.ToDictionary(row => row.Id, _ => new ObjectHistoryTarget(null, null, "unavailable"), StringComparer.Ordinal);
        foreach (var group in subjects.GroupBy(pair => pair.Value.Resource.ResourceKey))
        {
            var resource = group.First().Value.Resource;
            var ids = group.Select(pair => pair.Value.Id).Distinct(StringComparer.Ordinal).ToArray();
            var readable = await readableIds.GetReadableIdsAsync(resource.ResourceKey, ids, token);
            var admitted = new List<string>();
            foreach (var id in readable)
                if (resource.ReadConstraintKey is null || await access.IsReadableAsync(resource.ResourceKey, id, token)) admitted.Add(id);
            // 只有已通过当前read与owner约束的主体，才能交给当前名称目录或使用历史快照。
            IReadOnlyDictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            var directory = objectNames.SingleOrDefault(value => value.Kind == resource.ResourceKey);
            if (admitted.Count > 0 && directory is not null) names = await directory.FindManyAsync(admitted, token);
            else if (admitted.Count > 0 && crudResources.FindByResourceKey(resource.ResourceKey) is { } descriptor)
            {
                names = await db.Set<Dictionary<string, object?>>(resource.ModelName).AsNoTracking()
                    .Where(row => EF.Parameter(admitted).Contains(EF.Property<string>(row, resource.IdField)))
                    .Select(row => new { Id = EF.Property<string>(row, resource.IdField), Name = EF.Property<string?>(row, descriptor.DisplayField) })
                    .Where(row => row.Name != null).ToDictionaryAsync(row => row.Id, row => row.Name!, token);
            }
            var allowed = admitted.ToHashSet(StringComparer.Ordinal);
            var snapshots = new Dictionary<string, StoredTargetFacts>(StringComparer.Ordinal);
            var rowsById = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
            foreach (var records in group.Where(pair => allowed.Contains(pair.Value.Id))
                .GroupBy(pair => (rowsById[pair.Key].EventType, rowsById[pair.Key].Resource)))
            {
                var policy = selected.First(value => value.EventType == records.Key.EventType
                    && (value.RecordResource ?? resource.ResourceKey) == records.Key.Resource);
                if (policy.TextSnapshot is null) continue;
                var recordIds = records.Select(pair => pair.Key).ToArray();
                var facts = await TargetFacts(policy, resource.ModelName, records.Key.Resource, includeText: true)
                    .Where(row => EF.Parameter(recordIds).Contains(row.Id)).ToArrayAsync(token);
                foreach (var fact in facts) snapshots[fact.Id] = fact;
            }
            foreach (var (recordId, subject) in group)
            {
                if (!allowed.Contains(subject.Id)) continue;
                if (snapshots.GetValueOrDefault(recordId) is { HasSnapshot: true } snapshot)
                    result[recordId] = new(snapshot.Name, snapshot.Code, "historical");
                else result[recordId] = names.TryGetValue(subject.Id, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? new(name, null, "current") : new(null, null, "unrecorded");
            }
        }
        return result;
    }

    // 固定BusinessAuditField schema在SQL中提取owner明确许可的主体和text快照；不把FieldsJson搬到应用。
    private IQueryable<StoredTargetFacts> TargetFacts(ObjectHistoryEventPolicy policy, string model, string recordResource, bool includeText)
    {
        var subjectField = policy.SubjectField?.Field ?? "";
        var subjectBefore = policy.SubjectField?.Side == ObjectHistoryFieldSide.Before;
        var subjectKind = policy.SubjectField?.Kind ?? model;
        var nameField = includeText ? policy.TextSnapshot?.NameField ?? "" : "";
        var codeField = includeText ? policy.TextSnapshot?.CodeField ?? "" : "";
        return db.Database.SqlQuery<StoredTargetFacts>($"""
            SELECT b.[EID] AS [Id],
                (SELECT CASE WHEN {subjectBefore} = CAST(1 AS bit) THEN f.[BeforeValue] ELSE f.[AfterValue] END
                 FROM OPENJSON(CASE WHEN ISJSON(b.[FieldsJson]) = 1 THEN b.[FieldsJson] ELSE N'[]' END)
                 WITH ([FieldName] nvarchar(128) '$.Field',
                       [BeforeKind] nvarchar(128) '$.Before.Kind', [BeforeValue] nvarchar(max) '$.Before.Value',
                       [AfterKind] nvarchar(128) '$.After.Kind', [AfterValue] nvarchar(max) '$.After.Value') f
                 WHERE f.[FieldName] COLLATE Latin1_General_100_BIN2 = {subjectField}
                   AND (({subjectBefore} = CAST(1 AS bit) AND f.[BeforeKind] COLLATE Latin1_General_100_BIN2 = {subjectKind})
                     OR ({subjectBefore} = CAST(0 AS bit) AND f.[AfterKind] COLLATE Latin1_General_100_BIN2 = {subjectKind}))) AS [SubjectId],
                CAST(CASE WHEN EXISTS (SELECT 1
                 FROM OPENJSON(CASE WHEN ISJSON(b.[FieldsJson]) = 1 THEN b.[FieldsJson] ELSE N'[]' END)
                 WITH ([FieldName] nvarchar(128) '$.Field', [Kind] nvarchar(128) '$.After.Kind') f
                 WHERE f.[FieldName] COLLATE Latin1_General_100_BIN2 = {nameField} AND f.[Kind] = N'text') THEN 1 ELSE 0 END AS bit) AS [HasSnapshot],
                (SELECT f.[Value] FROM OPENJSON(CASE WHEN ISJSON(b.[FieldsJson]) = 1 THEN b.[FieldsJson] ELSE N'[]' END)
                 WITH ([FieldName] nvarchar(128) '$.Field', [Kind] nvarchar(128) '$.After.Kind', [Value] nvarchar(max) '$.After.Value') f
                 WHERE f.[FieldName] COLLATE Latin1_General_100_BIN2 = {nameField} AND f.[Kind] = N'text') AS [Name],
                (SELECT f.[Value] FROM OPENJSON(CASE WHEN ISJSON(b.[FieldsJson]) = 1 THEN b.[FieldsJson] ELSE N'[]' END)
                 WITH ([FieldName] nvarchar(128) '$.Field', [Kind] nvarchar(128) '$.After.Kind', [Value] nvarchar(max) '$.After.Value') f
                 WHERE f.[FieldName] COLLATE Latin1_General_100_BIN2 = {codeField} AND f.[Kind] = N'text') AS [Code]
            FROM [BusinessChange] b
            WHERE b.[EventType] COLLATE Latin1_General_100_BIN2 = {policy.EventType}
              AND b.[Resource] COLLATE Latin1_General_100_BIN2 = {recordResource}
            """);
    }

    private sealed class StoredTargetFacts
    {
        public required string Id { get; init; }
        public string? SubjectId { get; init; }
        public bool HasSnapshot { get; init; }
        public string? Name { get; init; }
        public string? Code { get; init; }
    }
}
