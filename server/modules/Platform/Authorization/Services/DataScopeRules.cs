using System.Text.Json;
using ICP.Platform.Modules.Authorization.Caching;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>规则根拥有数据库管理和实际差异审计；判权读取已发布的完整缓存。</summary>
public sealed class DataScopeRules(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    DataScopeCatalog catalog, IBusinessAuditRecorder audit, DataScopeIdentities identities, DataScopeRuleCache cache)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AsyncLocal<RuleReadFrame?> _reading = new();
    private DbSet<Dictionary<string, object?>> Nodes => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeRuleNode"));
    private DbSet<Dictionary<string, object?>> Subjects => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeRuleSubject"));

    /// <summary>管理列表在数据库分页，配置不随对象数量增长。</summary>
    public async Task<IReadOnlyList<DataScopeRuleDto>> ListAsync(int page, CancellationToken token)
    {
        var roots = await Nodes.AsNoTracking().Where(row => EF.Property<string?>(row, "ParentId") == null)
            .OrderBy(row => EF.Property<string>(row, "EID"))
            .Skip((Math.Max(1, page) - 1) * 20).Take(20).ToListAsync(token);
        if (roots.Count == 0) return [];
        var ids = roots.Select(row => (string)row["EID"]!).ToArray();
        var nodes = await Nodes.AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "RootId"))).ToListAsync(token);
        var trees = await MaterializeAsync(nodes, token);
        return ids.Select(id => trees.Single(tree => tree.Id == id)).ToArray();
    }

    /// <summary>在当前逻辑判权内延迟取得同一完整快照；配置故障不会回源或降级为未配置。</summary>
    public async Task<IReadOnlyList<DataScopeRuleDto>> ForResourceAsync(string resourceKey, CancellationToken token)
    {
        var frame = _reading.Value;
        if (frame is null) return (await cache.ReadAsync(token)).ForResource(resourceKey);
        Task<DataScopeRuleSnapshot> snapshot;
        lock (frame) snapshot = frame.Snapshot ??= cache.ReadAsync(token).AsTask();
        return (await snapshot.WaitAsync(token)).ForResource(resourceKey);
    }

    // 只覆盖一次逻辑判权；HTTP 或业务写入完成后的独立新判权会开启新租约。
    internal RuleReadOperation BeginOperation() => new(this, _reading.Value ?? new());

    internal sealed class RuleReadFrame
    {
        internal Task<DataScopeRuleSnapshot>? Snapshot;
    }

    internal sealed class RuleReadOperation : IDisposable
    {
        private readonly DataScopeRules _owner;
        private readonly RuleReadFrame _frame;
        private readonly RuleReadFrame? _previous;
        private bool _disposed;

        // 同步激活上下文，后续异步调用只共享配置引用，不共享允许结果。
        internal RuleReadOperation(DataScopeRules owner, RuleReadFrame frame)
        {
            _owner = owner; _frame = frame; _previous = owner._reading.Value;
            owner._reading.Value = frame;
        }

        // 异步枚举每次推进都显式激活同一帧，不能依赖 yield 将 AsyncLocal 流回调用者。
        internal RuleReadOperation Enter() => new(_owner, _frame);

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._reading.Value = _previous;
        }
    }

    /// <summary>校验后以根为目标取得数据库事务锁，实际前驱、配置和审计一起提交。</summary>
    public async Task SaveAsync(DataScopeRuleDto rule, CancellationToken cancellationToken)
    {
        catalog.Validate(rule);
        rule = catalog.NormalizeForSave(rule);
        rule = NormalizeNames(rule);
        var incoming = Flatten(rule).ToArray();
        foreach (var group in incoming.SelectMany(node => node.Subjects).Where(subject => subject.SubjectId is not null).GroupBy(subject => subject.Type))
        {
            foreach (var ids in group.Select(subject => subject.SubjectId!).Distinct(StringComparer.Ordinal).Chunk(100))
            {
                var names = await identities.SearchAsync(group.Key, null, 1, ids, cancellationToken);
                if (ids.Except(names.CurrentValues.Select(item => item.Id), StringComparer.Ordinal).Any())
                    throw new RolePermissionValidationException("所选授权主体不存在。", "DATA_SCOPE_SUBJECT_NOT_FOUND", field: "subjects");
            }
        }
        await transactions.ExecuteAsync(["DataScopeRuleNode", "DataScopeRuleSubject"], async token =>
        {
            // 所有根同属一层；跨根保存先串行核对名称，避免两个并发新建同时通过检查。
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource='Authorization.DataScopeRuleNames', @LockMode='Exclusive', @LockOwner='Transaction'; IF @result < 0 THROW 51000, 'Data scope rule name lock failed.', 1;", token);
            if (await Nodes.AsNoTracking().AnyAsync(row => EF.Property<string?>(row, "ParentId") == null
                && EF.Property<string>(row, "EID") != rule.Id
                && EF.Functions.Collate(EF.Property<string>(row, "Name").Trim(), "Latin1_General_100_BIN2") == rule.Name, token))
                throw new RolePermissionValidationException($"同级规则名称“{rule.Name}”已存在。", "DATA_SCOPE_RULE_NAME_DUPLICATE", new Dictionary<string, object?> { ["name"] = rule.Name }, "name");
            var target = "Authorization.DataScopeRule:" + rule.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={target}, @LockMode='Exclusive', @LockOwner='Transaction'; IF @result < 0 THROW 51000, 'Data scope rule lock failed.', 1;", token);
            var existing = await Nodes.Where(row => EF.Property<string>(row, "RootId") == rule.Id).ToListAsync(token);
            if (existing.Any(node => !string.Equals((string)node["RootId"]!, rule.Id, StringComparison.Ordinal)))
                throw new RolePermissionValidationException("规则身份必须与已保存的规范身份一致。", "DATA_SCOPE_RULE_CANONICAL_REQUIRED", field: "id");
            var old = existing.Count == 0 ? null : (await MaterializeAsync(existing, token)).Single();
            var canonical = Canonical(rule);
            var previous = old is null ? null : Canonical(old);
            if (previous == canonical) return true;
            var nodeIds = incoming.Select(node => node.Id).ToArray();
            var occupiedNodes = await Nodes.AsNoTracking().Where(row => nodeIds.Contains(EF.Property<string>(row, "EID")))
                .Select(row => new { Id = EF.Property<string>(row, "EID"), RootId = EF.Property<string>(row, "RootId") }).ToListAsync(token);
            if (occupiedNodes.Any(node => node.RootId != rule.Id || !nodeIds.Contains(node.Id, StringComparer.Ordinal)))
                throw new RolePermissionValidationException("规则节点身份已属于另一根规则。", "DATA_SCOPE_RULE_NODE_OCCUPIED", field: "id");
            var subjectIds = incoming.SelectMany(node => node.Subjects).Select(subject => subject.Id).ToArray();
            var oldNodeIds = existing.Select(node => (string)node["EID"]!).ToArray();
            var occupiedSubjects = await Subjects.AsNoTracking().Where(row => subjectIds.Contains(EF.Property<string>(row, "EID")))
                .Select(row => new { Id = EF.Property<string>(row, "EID"), NodeId = EF.Property<string>(row, "NodeId") }).ToListAsync(token);
            if (occupiedSubjects.Any(subject => !oldNodeIds.Contains(subject.NodeId, StringComparer.Ordinal)
                || !subjectIds.Contains(subject.Id, StringComparer.Ordinal)))
                throw new RolePermissionValidationException("主体授权项身份已属于另一规则。", "DATA_SCOPE_RULE_SUBJECT_OCCUPIED", field: "subjects");
            var rows = await Subjects.Where(row => oldNodeIds.Contains(EF.Property<string>(row, "NodeId"))).ToListAsync(token);
            Subjects.RemoveRange(rows.Where(row => !subjectIds.Contains((string)row["EID"]!, StringComparer.Ordinal)));
            Nodes.RemoveRange(existing.Where(node => !nodeIds.Contains((string)node["EID"]!, StringComparer.Ordinal)));
            foreach (var current in incoming)
            {
                var node = existing.SingleOrDefault(node => (string)node["EID"]! == current.Id);
                if (node is null) { node = new() { ["EID"] = current.Id }; Nodes.Add(node); }
                node["RootId"] = current.RootId; node["ParentId"] = current.ParentId; node["Name"] = current.Name;
                node["Enabled"] = current.Enabled; node["ResourceKey"] = current.ResourceKey;
                node["ConditionJson"] = JsonSerializer.Serialize(current.Condition, Json);
                foreach (var subject in current.Subjects)
                {
                    var row = rows.SingleOrDefault(row => (string)row["EID"]! == subject.Id);
                    if (row is null) { row = new() { ["EID"] = subject.Id }; Subjects.Add(row); }
                    row["NodeId"] = current.Id;
                    row["MatchOptionsJson"] = subject.MatchOptions is null ? null : JsonSerializer.Serialize(subject.MatchOptions, Json);
                    row["SubjectType"] = subject.Type; row["SubjectId"] = subject.SubjectId; row["Priority"] = subject.Priority;
                    row["ActionsJson"] = JsonSerializer.Serialize(subject.Actions.OrderBy(action => action.Key).ToDictionary(), Json);
                }
            }
            await audit.RegisterAsync(new("dataScope.rule.changed", "authorization.dataScopeRule", rule.Id,
                [new("Configuration", previous is null ? null : BusinessAuditValue.Text(previous), BusinessAuditValue.Text(canonical))]), token);
            return true;
        }, cancellationToken);
    }

    // 名称去除首尾空白后按原文判重；只约束兄弟，不禁止不同父节点下使用同名。
    private static DataScopeRuleDto NormalizeNames(DataScopeRuleDto node)
    {
        var children = node.Children.Select(NormalizeNames).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in children)
            if (!names.Add(child.Name))
                throw new RolePermissionValidationException($"同级规则名称“{child.Name}”已存在。", "DATA_SCOPE_RULE_NAME_DUPLICATE", new Dictionary<string, object?> { ["name"] = child.Name }, "name");
        return node with { Name = node.Name.Trim(), Children = children };
    }

    /// <summary>在根目标锁内删除完整子树及主体项，并以实际旧配置登记撤销审计。</summary>
    public async Task DeleteAsync(string rootId, CancellationToken cancellationToken)
    {
        await transactions.ExecuteAsync(["DataScopeRuleNode", "DataScopeRuleSubject"], async token =>
        {
            var target = "Authorization.DataScopeRule:" + rootId;
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={target}, @LockMode='Exclusive', @LockOwner='Transaction'; IF @result < 0 THROW 51000, 'Data scope rule lock failed.', 1;", token);
            var nodes = await Nodes.Where(row => EF.Property<string>(row, "RootId") == rootId).ToListAsync(token);
            if (nodes.Count == 0) return true;
            if (nodes.Any(node => !string.Equals((string)node["RootId"]!, rootId, StringComparison.Ordinal)))
                throw new RolePermissionValidationException("规则身份必须与已保存的规范身份一致。", "DATA_SCOPE_RULE_CANONICAL_REQUIRED", field: "id");
            var previous = Canonical((await MaterializeAsync(nodes, token)).Single());
            var ids = nodes.Select(node => (string)node["EID"]!).ToArray();
            Subjects.RemoveRange(await Subjects.Where(row => ids.Contains(EF.Property<string>(row, "NodeId"))).ToListAsync(token));
            Nodes.RemoveRange(nodes);
            await audit.RegisterAsync(new("dataScope.rule.changed", "authorization.dataScopeRule", rootId,
                [new("Configuration", BusinessAuditValue.Text(previous), BusinessAuditValue.Text(null))]), token);
            return true;
        }, cancellationToken);
    }

    // 稳定身份排序使集合次序变化不制造审计，也不影响显式优先级。
    private static string Canonical(DataScopeRuleDto rule) => JsonSerializer.Serialize(CanonicalNode(rule), Json);

    // 子节点和主体项分别按稳定身份排序，保留层级与显式优先级。
    private static DataScopeRuleDto CanonicalNode(DataScopeRuleDto node) => node with
    {
        Subjects = node.Subjects.OrderBy(subject => subject.Id, StringComparer.Ordinal).Select(subject => subject with
        { Actions = subject.Actions.OrderBy(action => action.Key, StringComparer.Ordinal).ToDictionary() }).ToArray(),
        Children = node.Children.OrderBy(child => child.Id, StringComparer.Ordinal).Select(CanonicalNode).ToArray()
    };

    // 整根事务逐节点保存，输入次序不改变父子身份。
    private static IEnumerable<DataScopeRuleDto> Flatten(DataScopeRuleDto node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }

    // 每批根一次读取主体项，不按每条业务对象读取授权目录。
    private async Task<IReadOnlyList<DataScopeRuleDto>> MaterializeAsync(IReadOnlyList<Dictionary<string, object?>> nodes, CancellationToken token)
    {
        if (nodes.Count == 0) return [];
        var ids = nodes.Select(row => (string)row["EID"]!).ToArray();
        var subjects = await Subjects.AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "NodeId"))).ToListAsync(token);
        return DataScopeRuleMaterializer.Build(nodes, subjects);
    }
}
