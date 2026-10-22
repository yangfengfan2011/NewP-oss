using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Authorization.Caching;

/// <summary>同一发布结果持有节点、主体、资源索引与已解析完整树；只复制变更分支。</summary>
public sealed class DataScopeRuleSnapshot
{
    internal static readonly DataScopeRuleSnapshot Empty = new();
    private static readonly ImmutableHashSet<string> EmptyIds = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
    private readonly ImmutableDictionary<string, FrozenDictionary<string, object?>> _nodes;
    private readonly ImmutableDictionary<string, FrozenDictionary<string, object?>> _subjects;
    private readonly ImmutableDictionary<string, ImmutableHashSet<string>> _resourceNodes;
    private readonly ImmutableDictionary<string, ImmutableHashSet<string>> _nodeSubjects;
    private readonly ImmutableDictionary<string, ImmutableHashSet<string>> _references;
    private readonly ImmutableDictionary<string, ImmutableArray<DataScopeRuleDto>> _trees;
    private readonly ImmutableDictionary<string, Exception> _errors;
    internal long RecordCount => (long)_nodes.Count + _subjects.Count;

    // 只有真正装载成功的空集合才由缓存发布；本对象本身不表示缓存已初始化。
    private DataScopeRuleSnapshot() : this(ImmutableDictionary.Create<string, FrozenDictionary<string, object?>>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, FrozenDictionary<string, object?>>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, ImmutableArray<DataScopeRuleDto>>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, Exception>(StringComparer.Ordinal)) { }

    // 全部不可变分支在同一结果中构造，发布方仅替换这一引用。
    private DataScopeRuleSnapshot(ImmutableDictionary<string, FrozenDictionary<string, object?>> nodes,
        ImmutableDictionary<string, FrozenDictionary<string, object?>> subjects,
        ImmutableDictionary<string, ImmutableHashSet<string>> resourceNodes,
        ImmutableDictionary<string, ImmutableHashSet<string>> nodeSubjects,
        ImmutableDictionary<string, ImmutableHashSet<string>> references,
        ImmutableDictionary<string, ImmutableArray<DataScopeRuleDto>> trees,
        ImmutableDictionary<string, Exception> errors)
    {
        _nodes = nodes; _subjects = subjects; _resourceNodes = resourceNodes; _nodeSubjects = nodeSubjects;
        _references = references; _trees = trees; _errors = errors;
    }

    /// <summary>取得当前完整结果中一个资源的全部根；合法无规则资源返回空集合。</summary>
    public IReadOnlyList<DataScopeRuleDto> ForResource(string resourceKey)
    {
        if (_errors.TryGetValue(resourceKey, out var error))
            throw new PlatformCacheUnavailableException(DataScopeRuleCache.CacheName, inner: error);
        return _trees.GetValueOrDefault(resourceKey, []);
    }

    // 实际节点和主体变更共同维护索引；旧、新资源都参与同一次构造和校验，不重新查询整表。
    internal DataScopeRuleSnapshot Apply(IEnumerable<PlatformRecordChange> changes, DataScopeCatalog catalog, CancellationToken token)
    {
        var builder = new Builder(this);
        foreach (var change in changes) builder.Add(change, token);
        return builder.Build(catalog, token);
    }

    /// <summary>以本结果为基底逐条登记变更；全量装载从 <see cref="Empty"/> 流式填充，校验只在构建时执行一次。</summary>
    internal sealed class Builder(DataScopeRuleSnapshot snapshot)
    {
        private readonly ImmutableDictionary<string, FrozenDictionary<string, object?>>.Builder _nodes = snapshot._nodes.ToBuilder();
        private readonly ImmutableDictionary<string, FrozenDictionary<string, object?>>.Builder _subjects = snapshot._subjects.ToBuilder();
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _resourceNodes = snapshot._resourceNodes.ToBuilder();
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _nodeSubjects = snapshot._nodeSubjects.ToBuilder();
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _references = snapshot._references.ToBuilder();
        private readonly HashSet<string> _affectedResources = new(StringComparer.Ordinal);
        private readonly HashSet<string> _affectedNodes = new(StringComparer.Ordinal);

        // 使用当前候选中的实际前值撤销旧边，再登记持久化后的完整记录；同一记录多次变更按顺序折叠。
        internal void Add(PlatformRecordChange change, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var records = change.ModelKey switch
            {
                DataScopeRuleCache.NodeModel => _nodes,
                DataScopeRuleCache.SubjectModel => _subjects,
                _ => throw new InvalidOperationException("DATA_SCOPE_CACHE_MODEL_INVALID")
            };
            if (records.TryGetValue(change.Id, out var before)) Index(change, before, false);
            if (change.After is null) records.Remove(change.Id);
            else
            {
                var row = change.After.ToFrozenDictionary(StringComparer.Ordinal);
                if (!Equals(row["EID"], change.Id)) throw new InvalidOperationException("DATA_SCOPE_CACHE_ID_INVALID");
                records[change.Id] = row;
                Index(change, row, true);
            }
        }

        // 只重建受影响资源；基底中的旧关系与候选中的新关系共同决定影响范围。
        internal DataScopeRuleSnapshot Build(DataScopeCatalog catalog, CancellationToken token)
        {
            var trees = snapshot._trees.ToBuilder();
            var errors = snapshot._errors.ToBuilder();
            foreach (var id in _affectedNodes)
            {
                token.ThrowIfCancellationRequested();
                if (snapshot._nodes.TryGetValue(id, out var previous)) _affectedResources.Add((string)previous["ResourceKey"]!);
                if (_nodes.TryGetValue(id, out var current)) _affectedResources.Add((string)current["ResourceKey"]!);
                else if (_nodeSubjects.TryGetValue(id, out var orphaned) && orphaned.Count > 0)
                    throw new InvalidOperationException("DATA_SCOPE_SUBJECT_NODE_INVALID");
                // 关系改变必须重建旧、新两端资源；不能让外资源的坏子树被按资源分桶后隐藏。
                foreach (var source in snapshot._references.GetValueOrDefault(id, EmptyIds).Union(_references.GetValueOrDefault(id, EmptyIds)))
                {
                    if (snapshot._nodes.TryGetValue(source, out var oldSource)) _affectedResources.Add((string)oldSource["ResourceKey"]!);
                    if (_nodes.TryGetValue(source, out var newSource)) _affectedResources.Add((string)newSource["ResourceKey"]!);
                }
            }
            foreach (var resource in _affectedResources)
            {
                token.ThrowIfCancellationRequested();
                var ids = _resourceNodes.GetValueOrDefault(resource, EmptyIds);
                // 资源已无节点时不再解析目录，删除已卸载模块的最后一条规则也能清除其错误。
                if (ids.IsEmpty)
                {
                    trees.Remove(resource);
                    errors.Remove(resource);
                    continue;
                }
                try
                {
                    // 未登记资源（如模块卸载后保留的规则）只封闭自身，规则留待重装后生效；目录装配故障仍由整体缓存恢复。
                    catalog.Resolve(resource);
                    if (ids.Any(id => _references.GetValueOrDefault(id, EmptyIds).Any(source =>
                        !Equals(_nodes[source]["ResourceKey"], resource))
                        || new[] { (string?)_nodes[id]["RootId"], (string?)_nodes[id]["ParentId"] }.OfType<string>().Any(target =>
                            _nodes.TryGetValue(target, out var row) && !Equals(row["ResourceKey"], resource))))
                        throw new InvalidOperationException("DATA_SCOPE_RULE_RESOURCE_RELATION_INVALID");
                    var roots = DataScopeRuleMaterializer.Build(ids.Select(id => _nodes[id]),
                        ids.SelectMany(id => _nodeSubjects.GetValueOrDefault(id, EmptyIds)).Select(id => _subjects[id]));
                    foreach (var root in roots) catalog.Validate(root);
                    if (roots.IsEmpty) trees.Remove(resource);
                    else trees[resource] = roots;
                    errors.Remove(resource);
                }
                catch (Exception error) when (error is JsonException or RolePermissionValidationException
                    || error is InvalidOperationException { Message: "DATA_SCOPE_CONDITION_INVALID" or "DATA_SCOPE_SUBJECT_INVALID"
                        or "DATA_SCOPE_TREE_INVALID" or "DATA_SCOPE_RULE_RESOURCE_RELATION_INVALID" })
                {
                    // 坏配置保留明确资源错误；停用节点同样完整验证，不发布旧树或空规则替代。
                    trees.Remove(resource);
                    errors[resource] = error;
                }
            }
            return new(_nodes.ToImmutable(), _subjects.ToImmutable(), _resourceNodes.ToImmutable(), _nodeSubjects.ToImmutable(),
                _references.ToImmutable(), trees.ToImmutable(), errors.ToImmutable());
        }

        // 节点维护资源与根/父引用索引，主体维护节点索引，并记录需要重建的节点和资源。
        private void Index(PlatformRecordChange change, IReadOnlyDictionary<string, object?> row, bool add)
        {
            if (change.ModelKey == DataScopeRuleCache.NodeModel)
            {
                var resource = (string)row["ResourceKey"]!;
                _affectedResources.Add(resource);
                _affectedNodes.Add(change.Id);
                UpdateIndex(_resourceNodes, resource, change.Id, add);
                foreach (var target in new[] { (string?)row["RootId"], (string?)row["ParentId"] }.OfType<string>())
                {
                    _affectedNodes.Add(target);
                    UpdateIndex(_references, target, change.Id, add);
                }
            }
            else
            {
                var node = (string)row["NodeId"]!;
                _affectedNodes.Add(node);
                UpdateIndex(_nodeSubjects, node, change.Id, add);
            }
        }
    }

    // 只维护直接成员，空桶直接删除，避免删除最后规则后残留旧资源索引。
    private static void UpdateIndex(ImmutableDictionary<string, ImmutableHashSet<string>>.Builder index,
        string key, string id, bool add)
    {
        var values = index.GetValueOrDefault(key, EmptyIds);
        values = add ? values.Add(id) : values.Remove(id);
        if (values.IsEmpty) index.Remove(key);
        else index[key] = values;
    }
}
