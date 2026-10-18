using System.Linq.Expressions;
using System.Globalization;
using ICP.Platform.Modeling;
using ICP.Platform.Presentation;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>对象主体关系及动作意见的单一持久化 owner；查询在数据库关联对象和当前主体。</summary>
public sealed class DataScopeObjectGrants(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    DataScopeCatalog catalog, DataScopeIdentities identities, IBusinessAuditRecorder audit, IPlatformObjectIdGenerator ids,
    IEntityAuditValueProvider auditValues, ITemporalInputNormalizer? temporal = null)
{
    private DbSet<Dictionary<string, object?>> Subjects => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeObjectSubject"));
    private DbSet<Dictionary<string, object?>> Actions => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeObjectAction"));

    /// <summary>仅读取指定规范资源和对象的完整配置，不依赖操作者是否仍能读取目标。</summary>
    public async Task<DataScopeObjectDto> GetAsync(string resourceKey, string objectId, CancellationToken token)
    {
        var rows = await Subjects.AsNoTracking().Where(row => EF.Property<string>(row, "ResourceKey") == resourceKey
            && EF.Property<string>(row, "ObjectId") == objectId).ToListAsync(token);
        return await MaterializeAsync(resourceKey, objectId, rows, token);
    }

    /// <summary>完整校验后按目标锁读取实际旧值；关系、意见和审计在一次事务中提交。</summary>
    public async Task SaveAsync(DataScopeObjectDto input, CancellationToken token)
    {
        var supportedActions = catalog.ActionsFor(input.ResourceKey);
        if (string.IsNullOrWhiteSpace(input.ObjectId) || input.ObjectId.Length > 128 || input.Subjects is null)
            throw new RolePermissionValidationException("对象授权目标无效。", "DATA_SCOPE_OBJECT_TARGET_INVALID", field: "objectId");
        var subjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectors = new HashSet<(string Type, string? Id, bool Descendants)>();
        foreach (var subject in input.Subjects)
        {
            if (string.IsNullOrWhiteSpace(subject.Id) || subject.Id.Length > 128 || !subjectIds.Add(subject.Id)
                || subject.Actions is null || subject.Actions.Any(action =>
                    !supportedActions.Any(defined => defined.Key == action.Key) || action.Value is not ("allow" or "deny" or "unset")))
                throw new RolePermissionValidationException("对象主体授权项或动作意见无效。", "DATA_SCOPE_OBJECT_SUBJECT_INVALID", field: "subjects");
            catalog.DirectSubjects.Validate(input.ResourceKey, subject.Type, subject.SubjectId, subject.MatchOptions);
            if (!selectors.Add((subject.Type, subject.SubjectId, subject.MatchOptions?.IncludeDescendants == true)))
                throw new RolePermissionValidationException("同一规范主体选择器不能重复。", "DATA_SCOPE_OBJECT_SUBJECT_DUPLICATE", field: "subjects");
        }
        foreach (var group in input.Subjects.Where(subject => subject.SubjectId is not null).GroupBy(subject => subject.Type))
            foreach (var batch in group.Select(subject => subject.SubjectId!).Distinct(StringComparer.Ordinal).Chunk(100))
            {
                var names = await identities.SearchAsync(group.Key, null, 1, batch, token);
                if (batch.Except(names.CurrentValues.Select(value => value.Id), StringComparer.Ordinal).Any())
                    throw new RolePermissionValidationException("所选授权主体不存在。", "DATA_SCOPE_SUBJECT_NOT_FOUND", field: "subjects");
            }
        var normalized = input with { Subjects = input.Subjects.Select(Normalize).ToArray() };
        // 配置与动作行的实际写入和业务记录共享本次可信stamp，不在保存后伪造审计时间。
        using var operation = auditValues.BeginOperation();
        await transactions.ExecuteAsync(async cancellationToken =>
        {
            await LockAsync(normalized.ResourceKey, normalized.ObjectId, cancellationToken);
            var existing = await Subjects.Where(row => EF.Property<string>(row, "ResourceKey") == normalized.ResourceKey
                && EF.Property<string>(row, "ObjectId") == normalized.ObjectId).ToListAsync(cancellationToken);
            if (existing.Any(row => (string)row["ResourceKey"]! != normalized.ResourceKey || (string)row["ObjectId"]! != normalized.ObjectId))
                throw new RolePermissionValidationException("对象授权目标必须使用规范身份。", "DATA_SCOPE_OBJECT_CANONICAL_REQUIRED", field: "objectId");
            var old = await MaterializeAsync(normalized.ResourceKey, normalized.ObjectId, existing, cancellationToken);
            var previous = Canonical(old);
            var current = Canonical(normalized);
            if (SemanticConfiguration(old) == SemanticConfiguration(normalized)) return true;
            var requestedIds = normalized.Subjects.Select(subject => subject.Id).ToArray();
            var occupied = await Subjects.AsNoTracking().Where(row => requestedIds.Contains(EF.Property<string>(row, "EID")))
                .Select(row => new { Id = EF.Property<string>(row, "EID"), Resource = EF.Property<string>(row, "ResourceKey"),
                    Object = EF.Property<string>(row, "ObjectId") }).ToListAsync(cancellationToken);
            if (occupied.Any(row => row.Resource != normalized.ResourceKey || row.Object != normalized.ObjectId
                || !requestedIds.Contains(row.Id, StringComparer.Ordinal)))
                throw new RolePermissionValidationException("对象主体授权项身份已被占用。", "DATA_SCOPE_OBJECT_SUBJECT_OCCUPIED", field: "subjects");
            var oldIds = existing.Select(row => (string)row["EID"]!).ToArray();
            var oldActions = await Actions.Where(row => oldIds.Contains(EF.Property<string>(row, "EntryId"))).ToListAsync(cancellationToken);
            Actions.RemoveRange(oldActions.Where(row => !requestedIds.Contains((string)row["EntryId"]!, StringComparer.Ordinal)
                || normalized.Subjects.Single(subject => subject.Id == (string)row["EntryId"]!).Actions
                    .GetValueOrDefault((string)row["ActionKey"]!) is not ("allow" or "deny")));
            Subjects.RemoveRange(existing.Where(row => !requestedIds.Contains((string)row["EID"]!, StringComparer.Ordinal)));
            foreach (var subject in normalized.Subjects)
            {
                var row = existing.SingleOrDefault(value => (string)value["EID"]! == subject.Id);
                if (row is null) { row = new() { ["EID"] = subject.Id }; Subjects.Add(row); }
                row["ResourceKey"] = normalized.ResourceKey;
                row["ObjectId"] = normalized.ObjectId;
                row["SubjectType"] = subject.Type;
                row["SubjectId"] = subject.SubjectId;
                row["IncludeDescendants"] = subject.MatchOptions?.IncludeDescendants ?? false;
                row["Priority"] = subject.Priority;
                row["ExpiresAt"] = subject.ExpiresAt is null ? null : ReadExpiry(subject.ExpiresAt);
                foreach (var action in subject.Actions.Where(action => action.Value is "allow" or "deny"))
                {
                    var opinion = oldActions.SingleOrDefault(value => (string)value["EntryId"]! == subject.Id
                        && (string)value["ActionKey"]! == action.Key);
                    if (opinion is null)
                    {
                        opinion = new() { ["EID"] = ids.NewId("DataScopeObjectAction"), ["EntryId"] = subject.Id, ["ActionKey"] = action.Key };
                        Actions.Add(opinion);
                    }
                    opinion["Opinion"] = action.Value;
                }
            }
            await audit.RegisterAsync(new("dataScope.object.changed", "authorization.dataScopeObject",
                normalized.ResourceKey + "/" + normalized.ObjectId,
                [new("Configuration", BusinessAuditValue.Text(previous), BusinessAuditValue.Text(current))]), cancellationToken);
            return true;
        }, token);
    }

    /// <summary>单对象完整动作裁决只读取该对象关系，匹配语义仍交已解析的主体事实 owner。</summary>
    public async Task<string?> OpinionAsync(string resourceKey, string objectId, string action,
        DataScopeResolvedIdentities facts, DateTime observedAt, CancellationToken token,
        Action<string, IReadOnlyList<DataScopeDirectSubjectDto>>? observe = null)
    {
        var config = await GetAsync(resourceKey, objectId, token);
        var active = config.Subjects.Where(subject => (subject.ExpiresAt is null || observedAt <= ReadExpiry(subject.ExpiresAt))
            && subject.Actions.TryGetValue(action, out var vote) && vote != "unset").ToArray();
        var types = active.Select(subject => subject.Type).Where(DataScopeDirectSubjects.IsDynamic).Distinct(StringComparer.Ordinal).ToArray();
        var matching = types.Length == 0 ? [] : await Subjects.AsNoTracking().Where(catalog.DirectSubjects.Membership(resourceKey, facts, types))
            .Where(row => EF.Property<string>(row, "ResourceKey") == resourceKey && EF.Property<string>(row, "ObjectId") == objectId)
            .Select(row => EF.Property<string>(row, "EID")).ToArrayAsync(token);
        var opinions = active.Where(subject => (DataScopeDirectSubjects.IsDynamic(subject.Type) ? matching.Contains(subject.Id, StringComparer.Ordinal)
                : facts.Matches(subject.Type, subject.SubjectId, subject.MatchOptions))).ToArray();
        if (opinions.Length == 0) return null;
        var priority = opinions.Max(subject => subject.Priority);
        var result = opinions.Any(subject => subject.Priority == priority && subject.Actions[action] == "deny") ? "deny" : "allow";
        observe?.Invoke(result, opinions.Where(subject => subject.Priority == priority).ToArray());
        return result;
    }

    /// <summary>只窄取当前资源或当前对象对动作有明确意见的主体类型，不读取对象身份全集。</summary>
    public Task<string[]> SubjectTypesAsync(string resourceKey, string action, string? objectId, DateTime observedAt, CancellationToken token)
    {
        var subjects = Subjects.AsNoTracking().Where(row => EF.Property<string>(row, "ResourceKey") == resourceKey
            && (EF.Property<DateTime?>(row, "ExpiresAt") == null || EF.Property<DateTime?>(row, "ExpiresAt") >= observedAt));
        if (objectId is not null)
            subjects = subjects.Where(row => EF.Property<string>(row, "ObjectId") == objectId);
        return (from subject in subjects
            join opinion in Actions.AsNoTracking() on EF.Property<string>(subject, "EID") equals EF.Property<string>(opinion, "EntryId")
            where EF.Property<string>(opinion, "ActionKey") == action
            select EF.Property<string>(subject, "SubjectType")).Distinct().ToArrayAsync(token);
    }

    /// <summary>对象读取覆盖以相关 EXISTS 和最高优先级比较留在 SQL，行数随分页而非授权全集增长。</summary>
    public (Expression Allow, Expression Deny) ReadOverrides(string resourceKey, ParameterExpression row,
        DataScopeResolvedIdentities facts, IReadOnlyList<string> subjectTypes, DateTime observedAt)
    {
        if (subjectTypes.Count == 0) return (Expression.Constant(false), Expression.Constant(false));
        var membership = catalog.DirectSubjects.Membership(resourceKey, facts, subjectTypes);
        var entries = Subjects.AsNoTracking().Where(membership)
            .Where(subject => EF.Property<string>(subject, "ResourceKey") == resourceKey
                && (EF.Property<DateTime?>(subject, "ExpiresAt") == null || EF.Property<DateTime?>(subject, "ExpiresAt") >= observedAt));
        var votes = from subject in entries
            join action in Actions.AsNoTracking() on EF.Property<string>(subject, "EID") equals EF.Property<string>(action, "EntryId")
            where EF.Property<string>(action, "ActionKey") == "read"
            select new { Subject = subject, Action = action };
        var idField = catalog.Resolve(resourceKey).IdField;
        Expression<Func<Dictionary<string, object?>, bool>> allow = item => votes.Any(vote =>
            EF.Property<string>(vote.Subject, "ObjectId") == EF.Property<string>(item, idField)
            && EF.Property<string>(vote.Action, "Opinion") == "allow"
            && !votes.Any(other => EF.Property<string>(other.Subject, "ObjectId") == EF.Property<string>(vote.Subject, "ObjectId")
                && (EF.Property<long>(other.Subject, "Priority") > EF.Property<long>(vote.Subject, "Priority")
                    || EF.Property<long>(other.Subject, "Priority") == EF.Property<long>(vote.Subject, "Priority")
                    && EF.Property<string>(other.Action, "Opinion") == "deny")));
        Expression<Func<Dictionary<string, object?>, bool>> deny = item => votes.Any(vote =>
            EF.Property<string>(vote.Subject, "ObjectId") == EF.Property<string>(item, idField)
            && EF.Property<string>(vote.Action, "Opinion") == "deny"
            && !votes.Any(other => EF.Property<string>(other.Subject, "ObjectId") == EF.Property<string>(vote.Subject, "ObjectId")
                && EF.Property<long>(other.Subject, "Priority") > EF.Property<long>(vote.Subject, "Priority")));
        return (new ParameterSwap(allow.Parameters[0], row).Visit(allow.Body)!,
            new ParameterSwap(deny.Parameters[0], row).Visit(deny.Body)!);
    }

    // 技术行身份不代表权限变化；匹配选项参与选择器身份，动作、优先级和期限是可替换内容。
    private static string SemanticConfiguration(DataScopeObjectDto config) => JsonSerializer.Serialize(config.Subjects
        .OrderBy(subject => subject.Type, StringComparer.Ordinal).ThenBy(subject => subject.SubjectId, StringComparer.Ordinal)
        .ThenBy(subject => subject.MatchOptions?.IncludeDescendants == true)
        .Select(subject => new { subject.Type, subject.SubjectId,
            IncludeDescendants = subject.MatchOptions?.IncludeDescendants == true, subject.Priority, subject.ExpiresAt,
            Actions = subject.Actions.Where(action => action.Value != "unset").OrderBy(action => action.Key, StringComparer.Ordinal)
                .ToDictionary(action => action.Key, action => action.Value) }));

    // 审计保留真实配置行身份，不用语义比较投影替代历史事实。

    private static string Canonical(DataScopeObjectDto config) => JsonSerializer.Serialize(config with
    {
        Subjects = config.Subjects.OrderBy(subject => subject.Id, StringComparer.Ordinal).Select(subject => subject with
        {
            Actions = subject.Actions.Where(action => action.Value != "unset").OrderBy(action => action.Key, StringComparer.Ordinal)
                .ToDictionary(action => action.Key, action => action.Value),
            MatchOptions = subject.MatchOptions?.IncludeDescendants == true ? subject.MatchOptions : null
        }).ToArray()
    });

    // owner先持有同一ACL事务锁再检查对象资格；旧保存仍复用完全相同的锁身份和SQL。
    internal async Task LockAsync(string resourceKey, string objectId, CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("DATA_SCOPE_OBJECT_TRANSACTION_REQUIRED");
        var target = "Authorization.DataScopeObject:" + resourceKey + ":" + objectId;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={target}, @LockMode='Exclusive', @LockOwner='Transaction'; IF @result < 0 THROW 51000, 'Data scope object lock failed.', 1;",
            token);
    }

    // 一次目标读取批量取其动作行，并在应用层还原完整可编辑配置。
    private async Task<DataScopeObjectDto> MaterializeAsync(string resourceKey, string objectId,
        IReadOnlyList<Dictionary<string, object?>> rows, CancellationToken token)
    {
        var ids = rows.Select(row => (string)row["EID"]!).ToArray();
        var actions = await Actions.AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "EntryId"))).ToListAsync(token);
        var byEntry = actions.ToLookup(row => (string)row["EntryId"]!);
        return new(resourceKey, objectId, rows.Select(row => new DataScopeDirectSubjectDto((string)row["EID"]!,
            (string)row["SubjectType"]!, (string?)row["SubjectId"], (long)row["Priority"]!,
            byEntry[(string)row["EID"]!].ToDictionary(action => (string)action["ActionKey"]!, action => (string)action["Opinion"]!),
            (bool)row["IncludeDescendants"]! ? new(true) : null,
            row["ExpiresAt"] is DateTime expiry ? FormatExpiry(expiry) : null)).ToArray());
    }

    // Instant 输入与标准 CRUD 共用可信时区，规范值同时用于持久化、比较和无变化审计。
    private DataScopeDirectSubjectDto Normalize(DataScopeDirectSubjectDto subject)
    {
        string? expiresAt = null;
        if (subject.ExpiresAt is not null)
        {
            var normalizer = temporal ?? throw new InvalidOperationException("DATA_SCOPE_TEMPORAL_NORMALIZER_REQUIRED");
            var result = normalizer.Normalize(FieldValueType.Instant, subject.ExpiresAt);
            if (!result.IsSuccess || result.Value is not DateTime utc)
                throw new RolePermissionValidationException(result.Message ?? "授权期限无效。",
                    result.ErrorCode ?? "TEMPORAL_INPUT_INVALID", field: "subjects.expiresAt");
            expiresAt = FormatExpiry(utc);
        }
        return subject with { ExpiresAt = expiresAt,
            MatchOptions = subject.MatchOptions?.IncludeDescendants == true ? subject.MatchOptions : null };
    }

    // 只解析服务端规范化后存储或返回的 UTC 秒，不重新解释会话时区。
    private static DateTime ReadExpiry(string value) => DateTime.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'",
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    // SQL datetime2 不保留 Kind；协议仍明确输出 UTC 秒，空期限由 DTO 省略。
    private static string FormatExpiry(DateTime value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // 两个组合谓词共享调用方的业务行参数，避免由外层 Invoke 破坏 EF 翻译。
    private sealed class ParameterSwap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        // 仅替换业务行参数，子查询自己的主体与动作参数保持原样。
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
