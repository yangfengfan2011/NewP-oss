using System.Linq.Expressions;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>对象主体关系及动作意见的单一持久化 owner；查询在数据库关联对象和当前主体。</summary>
public sealed class DataScopeObjectGrants(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    DataScopeCatalog catalog, DataScopeIdentities identities, IBusinessAuditRecorder audit, IPlatformObjectIdGenerator ids)
{
    private DbSet<Dictionary<string, object?>> Subjects => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Authorization/DataScopeObjectSubject"));
    private DbSet<Dictionary<string, object?>> Actions => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Authorization/DataScopeObjectAction"));

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
        catalog.Resolve(input.ResourceKey);
        if (string.IsNullOrWhiteSpace(input.ObjectId) || input.ObjectId.Length > 128 || input.Subjects is null)
            throw new RolePermissionValidationException("对象授权目标无效。");
        var subjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subject in input.Subjects)
        {
            if (string.IsNullOrWhiteSpace(subject.Id) || subject.Id.Length > 128 || !subjectIds.Add(subject.Id)
                || subject.Actions is null || subject.Actions.Any(action =>
                    !catalog.Actions.Any(defined => defined.Key == action.Key) || action.Value is not ("allow" or "deny" or "unset")))
                throw new RolePermissionValidationException("对象主体授权项或动作意见无效。");
            identities.Validate(subject.Type, subject.SubjectId, subject.MatchOptions);
        }
        foreach (var group in input.Subjects.Where(subject => subject.SubjectId is not null).GroupBy(subject => subject.Type))
            foreach (var batch in group.Select(subject => subject.SubjectId!).Distinct(StringComparer.Ordinal).Chunk(100))
            {
                var names = await identities.SearchAsync(group.Key, null, 1, batch, token);
                if (batch.Except(names.CurrentValues.Select(value => value.Id), StringComparer.Ordinal).Any())
                    throw new RolePermissionValidationException("所选授权主体不存在。");
            }
        var normalized = input with { Subjects = input.Subjects.Select(subject => subject.MatchOptions?.IncludeDescendants == false
            ? subject with { MatchOptions = null } : subject).ToArray() };
        await transactions.ExecuteAsync(async cancellationToken =>
        {
            var target = "Authorization.DataScopeObject:" + normalized.ResourceKey + ":" + normalized.ObjectId;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={target}, @LockMode='Exclusive', @LockOwner='Transaction'; IF @result < 0 THROW 51000, 'Data scope object lock failed.', 1;",
                cancellationToken);
            var existing = await Subjects.Where(row => EF.Property<string>(row, "ResourceKey") == normalized.ResourceKey
                && EF.Property<string>(row, "ObjectId") == normalized.ObjectId).ToListAsync(cancellationToken);
            if (existing.Any(row => (string)row["ResourceKey"]! != normalized.ResourceKey || (string)row["ObjectId"]! != normalized.ObjectId))
                throw new RolePermissionValidationException("对象授权目标必须使用规范身份。");
            var old = await MaterializeAsync(normalized.ResourceKey, normalized.ObjectId, existing, cancellationToken);
            var previous = Canonical(old);
            var current = Canonical(normalized);
            if (previous == current) return true;
            var requestedIds = normalized.Subjects.Select(subject => subject.Id).ToArray();
            var occupied = await Subjects.AsNoTracking().Where(row => requestedIds.Contains(EF.Property<string>(row, "EID")))
                .Select(row => new { Id = EF.Property<string>(row, "EID"), Resource = EF.Property<string>(row, "ResourceKey"),
                    Object = EF.Property<string>(row, "ObjectId") }).ToListAsync(cancellationToken);
            if (occupied.Any(row => row.Resource != normalized.ResourceKey || row.Object != normalized.ObjectId
                || !requestedIds.Contains(row.Id, StringComparer.Ordinal)))
                throw new RolePermissionValidationException("对象主体授权项身份已被占用。");
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
        DataScopeResolvedIdentities facts, CancellationToken token)
    {
        var config = await GetAsync(resourceKey, objectId, token);
        var opinions = config.Subjects.Where(subject => subject.Actions.TryGetValue(action, out var vote) && vote != "unset"
            && facts.Matches(subject.Type, subject.SubjectId, subject.MatchOptions)).ToArray();
        if (opinions.Length == 0) return null;
        var priority = opinions.Max(subject => subject.Priority);
        return opinions.Any(subject => subject.Priority == priority && subject.Actions[action] == "deny") ? "deny" : "allow";
    }

    /// <summary>对象读取覆盖以相关 EXISTS 和最高优先级比较留在 SQL，行数随分页而非授权全集增长。</summary>
    public (Expression Allow, Expression Deny) ReadOverrides(string resourceKey, ParameterExpression row,
        DataScopeResolvedIdentities facts)
    {
        var membership = MembershipPredicate(facts);
        var entries = Subjects.AsNoTracking().Where(membership)
            .Where(subject => EF.Property<string>(subject, "ResourceKey") == resourceKey);
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

    // 当前事实仅扩展为 SQL 对关系行的筛选，不读取对象 ID；无匹配主体时返回恒假。
    private Expression<Func<Dictionary<string, object?>, bool>> MembershipPredicate(DataScopeResolvedIdentities facts)
    {
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "subject");
        Expression expression = Expression.Constant(false);
        foreach (var membership in identities.SubjectTypes.SelectMany(type => facts.Memberships(type.Key)))
        {
            var type = Equal("SubjectType", membership.Type);
            var id = membership.Id is null
                ? Expression.Equal(Property<string>("SubjectId"), Expression.Constant(null, typeof(string)))
                : Equal("SubjectId", membership.Id);
            var match = Expression.AndAlso(type, id);
            if (membership.RequiresDescendants) match = Expression.AndAlso(match, Property<bool>("IncludeDescendants"));
            expression = Expression.OrElse(expression, match);
        }
        return Expression.Lambda<Func<Dictionary<string, object?>, bool>>(expression, row);

        // 字段引用保持 EF 模型属性，数据库可按主体和资源索引执行。
        Expression Property<T>(string name) => Expression.Call(typeof(EF), nameof(EF.Property), [typeof(T)], row, Expression.Constant(name));
        // 规范身份比较沿数据库字段既有排序规则。
        Expression Equal(string name, string value) => Expression.Equal(Property<string>(name), Expression.Constant(value));
    }

    // 稳定排序使数组顺序和 unset 的显式写法都不制造重复审计。
    private static string Canonical(DataScopeObjectDto config) => JsonSerializer.Serialize(config with
    {
        Subjects = config.Subjects.OrderBy(subject => subject.Id, StringComparer.Ordinal).Select(subject => subject with
        {
            Actions = subject.Actions.Where(action => action.Value != "unset").OrderBy(action => action.Key, StringComparer.Ordinal)
                .ToDictionary(action => action.Key, action => action.Value),
            MatchOptions = subject.MatchOptions?.IncludeDescendants == true ? subject.MatchOptions : null
        }).ToArray()
    });

    // 一次目标读取批量取其动作行，并在应用层还原完整可编辑配置。
    private async Task<DataScopeObjectDto> MaterializeAsync(string resourceKey, string objectId,
        IReadOnlyList<Dictionary<string, object?>> rows, CancellationToken token)
    {
        var ids = rows.Select(row => (string)row["EID"]!).ToArray();
        var actions = await Actions.AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "EntryId"))).ToListAsync(token);
        var byEntry = actions.ToLookup(row => (string)row["EntryId"]!);
        return new(resourceKey, objectId, rows.Select(row => new DataScopeSubjectDto((string)row["EID"]!,
            (string)row["SubjectType"]!, (string?)row["SubjectId"], (long)row["Priority"]!,
            byEntry[(string)row["EID"]!].ToDictionary(action => (string)action["ActionKey"]!, action => (string)action["Opinion"]!),
            (bool)row["IncludeDescendants"]! ? new(true) : null)).ToArray());
    }

    // 两个组合谓词共享调用方的业务行参数，避免由外层 Invoke 破坏 EF 翻译。
    private sealed class ParameterSwap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        // 仅替换业务行参数，子查询自己的主体与动作参数保持原样。
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
