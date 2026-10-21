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
        var nodes = _nodes.ToBuilder();
        var subjects = _subjects.ToBuilder();
        var resourceNodes = _resourceNodes.ToBuilder();
        var nodeSubjects = _nodeSubjects.ToBuilder();
        var references = _references.ToBuilder();
        var trees = _trees.ToBuilder();
        var errors = _errors.ToBuilder();
        var affectedResources = new HashSet<string>(StringComparer.Ordinal);
        var affectedNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            token.ThrowIfCancellationRequested();
            var records = change.ModelKey switch
            {
                DataScopeRuleCache.NodeModel => nodes,
                DataScopeRuleCache.SubjectModel => subjects,
                _ => throw new InvalidOperationException("DATA_SCOPE_CACHE_MODEL_INVALID")
            };
            if (records.TryGetValue(change.Id, out var before)) Index(before, false);
            if (change.After is null) records.Remove(change.Id);
            else
            {
                var row = change.After.ToFrozenDictionary(StringComparer.Ordinal);
                if (!Equals(row["EID"], change.Id)) throw new InvalidOperationException("DATA_SCOPE_CACHE_ID_INVALID");
                records[change.Id] = row;
                Index(row, true);
            }

            // 使用缓存中的实际前值撤销旧边，再登记持久化后的完整记录。
            void Index(IReadOnlyDictionary<string, object?> row, bool add)
            {
                if (change.ModelKey == DataScopeRuleCache.NodeModel)
                {
                    var resource = (string)row["ResourceKey"]!;
                    affectedResources.Add(resource);
                    affectedNodes.Add(change.Id);
                    UpdateIndex(resourceNodes, resource, change.Id, add);
                    foreach (var target in new[] { (string?)row["RootId"], (string?)row["ParentId"] }.OfType<string>())
                    {
                        affectedNodes.Add(target);
                        UpdateIndex(references, target, change.Id, add);
                    }
                }
                else
                {
                    var node = (string)row["NodeId"]!;
                    affectedNodes.Add(node);
                    UpdateIndex(nodeSubjects, node, change.Id, add);
                }
            }
        }
        foreach (var id in affectedNodes)
        {
            token.ThrowIfCancellationRequested();
            if (_nodes.TryGetValue(id, out var previous)) affectedResources.Add((string)previous["ResourceKey"]!);
            if (nodes.TryGetValue(id, out var current)) affectedResources.Add((string)current["ResourceKey"]!);
            else if (nodeSubjects.TryGetValue(id, out var orphaned) && orphaned.Count > 0)
                throw new InvalidOperationException("DATA_SCOPE_SUBJECT_NODE_INVALID");
            // 关系改变必须重建旧、新两端资源；不能让外资源的坏子树被按资源分桶后隐藏。
            foreach (var source in _references.GetValueOrDefault(id, EmptyIds).Union(references.GetValueOrDefault(id, EmptyIds)))
            {
                if (_nodes.TryGetValue(source, out var oldSource)) affectedResources.Add((string)oldSource["ResourceKey"]!);
                if (nodes.TryGetValue(source, out var newSource)) affectedResources.Add((string)newSource["ResourceKey"]!);
            }
        }
        foreach (var resource in affectedResources)
        {
            token.ThrowIfCancellationRequested();
            // 未登记资源不能建立可信归属；目录/装配故障也仍由整体缓存恢复，不局部吞掉。
            catalog.Resolve(resource);
            var ids = resourceNodes.GetValueOrDefault(resource, EmptyIds);
            try
            {
                if (ids.Any(id => references.GetValueOrDefault(id, EmptyIds).Any(source =>
                    !Equals(nodes[source]["ResourceKey"], resource))
                    || new[] { (string?)nodes[id]["RootId"], (string?)nodes[id]["ParentId"] }.OfType<string>().Any(target =>
                        nodes.TryGetValue(target, out var row) && !Equals(row["ResourceKey"], resource))))
                    throw new InvalidOperationException("DATA_SCOPE_RULE_RESOURCE_RELATION_INVALID");
                var roots = DataScopeRuleMaterializer.Build(ids.Select(id => nodes[id]),
                    ids.SelectMany(id => nodeSubjects.GetValueOrDefault(id, EmptyIds)).Select(id => subjects[id]));
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
        return new(nodes.ToImmutable(), subjects.ToImmutable(), resourceNodes.ToImmutable(), nodeSubjects.ToImmutable(),
            references.ToImmutable(), trees.ToImmutable(), errors.ToImmutable());
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
