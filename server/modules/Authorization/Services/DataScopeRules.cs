using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>规则根拥有完整配置的保存和实际差异审计；读取不跨请求缓存。</summary>
public sealed class DataScopeRules(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    DataScopeCatalog catalog, IBusinessAuditRecorder audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DbSet<Dictionary<string, object?>> Nodes => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeRuleNode"));
    private DbSet<Dictionary<string, object?>> Subjects => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeRuleSubject"));

    /// <summary>管理列表在数据库分页，配置不随对象数量增长。</summary>
    public async Task<IReadOnlyList<DataScopeRuleDto>> ListAsync(int page, CancellationToken token)
    {
        var nodes = await Nodes.AsNoTracking().OrderBy(row => EF.Property<string>(row, "EID"))
            .Skip((Math.Max(1, page) - 1) * 20).Take(20).ToListAsync(token);
        return await MaterializeAsync(nodes, token);
    }

    /// <summary>只取当前目标资源的规则；配置读取失败不会降级为未配置。</summary>
    public async Task<IReadOnlyList<DataScopeRuleDto>> ForResourceAsync(string resourceKey, CancellationToken token) =>
        await MaterializeAsync(await Nodes.AsNoTracking().Where(row => EF.Property<string>(row, "ResourceKey") == resourceKey)
            .ToListAsync(token), token);

    /// <summary>校验后以根为目标取得数据库事务锁，实际前驱、配置和审计一起提交。</summary>
    public async Task SaveAsync(DataScopeRuleDto rule, CancellationToken cancellationToken)
    {
        catalog.Validate(rule);
        await transactions.ExecuteAsync(async token =>
        {
            var target = "Authorization.DataScopeRule:" + rule.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={target}, @LockMode='Exclusive', @LockOwner='Transaction'; IF @result < 0 THROW 51000, 'Data scope rule lock failed.', 1;", token);
            var node = await Nodes.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == rule.Id, token);
            var old = node is null ? null : (await MaterializeAsync([node], token)).Single();
            var canonical = Canonical(rule);
            var previous = old is null ? null : Canonical(old);
            if (previous == canonical) return true;
            var ids = rule.Subjects.Select(subject => subject.Id).ToArray();
            if (await Subjects.AnyAsync(row => ids.Contains(EF.Property<string>(row, "EID")) && EF.Property<string>(row, "NodeId") != rule.Id, token))
                throw new RolePermissionValidationException("主体授权项身份已属于另一规则。");
            if (node is null) { node = new() { ["EID"] = rule.Id }; Nodes.Add(node); }
            node["RootId"] = rule.RootId; node["ParentId"] = rule.ParentId; node["Name"] = rule.Name;
            node["Enabled"] = rule.Enabled; node["ResourceKey"] = rule.ResourceKey;
            node["ConditionJson"] = JsonSerializer.Serialize(rule.Condition, Json);
            var rows = await Subjects.Where(row => EF.Property<string>(row, "NodeId") == rule.Id).ToListAsync(token);
            Subjects.RemoveRange(rows.Where(row => !ids.Contains((string)row["EID"]!, StringComparer.Ordinal)));
            foreach (var subject in rule.Subjects)
            {
                var row = rows.SingleOrDefault(row => (string)row["EID"]! == subject.Id);
                if (row is null) { row = new() { ["EID"] = subject.Id, ["NodeId"] = rule.Id }; Subjects.Add(row); }
                row["SubjectType"] = subject.Type; row["SubjectId"] = subject.SubjectId; row["Priority"] = subject.Priority;
                row["ActionsJson"] = JsonSerializer.Serialize(subject.Actions.OrderBy(action => action.Key).ToDictionary(), Json);
            }
            await audit.RegisterAsync(new("dataScope.rule.changed", "authorization.dataScopeRule", rule.Id,
                [new("Configuration", previous is null ? null : BusinessAuditValue.Text(previous), BusinessAuditValue.Text(canonical))]), token);
            return true;
        }, cancellationToken);
    }

    // 稳定身份排序使集合次序变化不制造审计，也不影响显式优先级。
    private static string Canonical(DataScopeRuleDto rule) => JsonSerializer.Serialize(rule with
    {
        Subjects = rule.Subjects.OrderBy(subject => subject.Id, StringComparer.Ordinal).Select(subject => subject with
        { Actions = subject.Actions.OrderBy(action => action.Key, StringComparer.Ordinal).ToDictionary() }).ToArray()
    }, Json);

    // 每批根一次读取主体项，不按每条业务对象读取授权目录。
    private async Task<IReadOnlyList<DataScopeRuleDto>> MaterializeAsync(IReadOnlyList<Dictionary<string, object?>> nodes, CancellationToken token)
    {
        if (nodes.Count == 0) return [];
        var ids = nodes.Select(row => (string)row["EID"]!).ToArray();
        var subjects = await Subjects.AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "NodeId"))).ToListAsync(token);
        var grouped = subjects.ToLookup(row => (string)row["NodeId"]!);
        return nodes.Select(row => new DataScopeRuleDto((string)row["EID"]!, (string)row["RootId"]!, (string?)row["ParentId"],
            (string)row["Name"]!, (bool)row["Enabled"]!, (string)row["ResourceKey"]!,
            JsonSerializer.Deserialize<DataScopeConditionDto>((string)row["ConditionJson"]!, Json) ?? throw new InvalidOperationException("DATA_SCOPE_CONDITION_INVALID"),
            grouped[(string)row["EID"]!].Select(subject => new DataScopeSubjectDto((string)subject["EID"]!, (string)subject["SubjectType"]!,
                (string?)subject["SubjectId"], (long)subject["Priority"]!,
                JsonSerializer.Deserialize<Dictionary<string, string>>((string)subject["ActionsJson"]!, Json) ?? throw new InvalidOperationException("DATA_SCOPE_SUBJECT_INVALID"))).ToArray(), [])).ToArray();
    }
}
