using System.Text.Json;
using System.Text.RegularExpressions;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>标准关系树的静态读取配置；字段只引用模型属性，不接受表达式或 SQL。</summary>
public sealed record RelationTreeSource
{
    public IReadOnlyList<RelationTreeNodeSource> Nodes { get; init; } = [];
}

/// <summary>节点的实体投影和有序父关系；后备关系仅在前一父字段为空时使用。</summary>
public sealed record RelationTreeNodeSource
{
    public required string Key { get; init; }
    public required string Model { get; init; }
    public required string ResourceKey { get; init; }
    public required string IdField { get; init; }
    public required string LabelField { get; init; }
    public required string CodeField { get; init; }
    public required string SortField { get; init; }
    public string? EnabledField { get; init; }
    public bool RootWhenParentNull { get; init; }
    public IReadOnlyList<RelationTreeParentSource> Parents { get; init; } = [];
    public IReadOnlyList<RelationTreeFilter> Filters { get; init; } = [];
    public IReadOnlyDictionary<string, string> ContextFields { get; init; } = new Dictionary<string, string>();
}

/// <summary>节点集合的静态相交条件；值只允许 JSON 标量。</summary>
public sealed record RelationTreeFilter
{
    public required string Field { get; init; }
    public required string Operator { get; init; }
    public JsonElement Value { get; init; }
}

/// <summary>用外键字段定位父实体；后备父关系必须显式标记。</summary>
public sealed record RelationTreeParentSource
{
    public required string NodeType { get; init; }
    public required string Field { get; init; }
    public bool Fallback { get; init; }
}

/// <summary>已签发模块的树配置及其所有者。</summary>
public sealed record RelationTreeDefinition(string ModuleId, string ViewKey, RelationTreeSource Source);

/// <summary>标准树配置的静态验证与签发快照读取。</summary>
public static class RelationTreeMetadata
{
    public const string ProviderKey = "platform.relations";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>读取配置并验证节点类型对应关系；不从外键自动推断树。</summary>
    public static RelationTreeSource Read(JsonElement view)
    {
        var source = view.GetProperty("relationSource").Deserialize<RelationTreeSource>(JsonOptions)
            ?? throw new JsonException("Missing relation tree source.");
        Validate(source, view.GetProperty("nodeTypes").EnumerateArray().Select(node => node.GetProperty("key").GetString()!).ToArray());
        return source;
    }

    /// <summary>拒绝不完整字段、歧义父级、重复节点和不存在的父类型。</summary>
    public static void Validate(RelationTreeSource source, IReadOnlyList<string> nodeTypes)
    {
        if (source.Nodes.Count == 0 || source.Nodes.Select(node => node.Key).Distinct(StringComparer.Ordinal).Count() != source.Nodes.Count
            || !source.Nodes.Select(node => node.Key).Order().SequenceEqual(nodeTypes.Order()))
            throw new JsonException("Relation source must describe every tree node type exactly once.");
        foreach (var node in source.Nodes)
        {
            foreach (var name in new[] { node.Key, node.Model, node.IdField, node.LabelField, node.CodeField, node.SortField }
                .Concat(node.EnabledField is null ? [] : new[] { node.EnabledField })
                .Concat(node.ContextFields.Keys).Concat(node.ContextFields.Values).Concat(node.Parents.Select(parent => parent.Field)).Concat(node.Filters.Select(filter => filter.Field)))
                if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                    throw new JsonException($"Invalid relation tree identifier: {name}");
            if (string.IsNullOrWhiteSpace(node.ResourceKey)) throw new JsonException("Missing tree resource key.");
            foreach (var filter in node.Filters)
                if (filter.Operator is not ("=" or "contains" or "isNull" or "isNotNull")
                    || (filter.Operator is "=" or "contains" && filter.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)))
                    throw new JsonException("Tree filter requires a supported operator and scalar value.");
            for (var index = 0; index < node.Parents.Count; index++)
            {
                var parent = node.Parents[index];
                if (!nodeTypes.Contains(parent.NodeType, StringComparer.Ordinal) || parent.Fallback != (index > 0)
                    || node.Parents.Take(index).Any(previous => previous.Field == parent.Field))
                    throw new JsonException($"Invalid or ambiguous tree parent: {node.Key}");
            }
        }
        if (!source.Nodes.Any(node => node.Parents.Count == 0 || node.RootWhenParentNull)) throw new JsonException("Relation tree requires a root type.");
        var reachable = source.Nodes.Where(node => node.Parents.Count == 0 || node.RootWhenParentNull).Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            var additions = source.Nodes.Where(node => !reachable.Contains(node.Key) && node.Parents.Any(parent => reachable.Contains(parent.NodeType))).ToArray();
            if (additions.Length == 0) break;
            foreach (var node in additions) reachable.Add(node.Key);
        }
        if (reachable.Count != source.Nodes.Count) throw new JsonException("Tree node type has no path to a root type.");
    }

    /// <summary>从已认证内建模块的冻结字节投影配置，不重新读取可变 authoring 路径。</summary>
    public static IReadOnlyList<RelationTreeDefinition> Project(CanonicalModulePlan plan)
    {
        var definitions = new List<RelationTreeDefinition>();
        foreach (var module in plan.Modules)
        {
            var snapshot = module.Provenance.GetAuthenticatedBuiltInSnapshot(module);
            if (snapshot is null) continue;
            foreach (var path in snapshot.EnumerateFiles("metadata/views").Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                using var document = JsonDocument.Parse(snapshot.ReadFile(path));
                if (!document.RootElement.TryGetProperty("view", out var view)
                    || !view.TryGetProperty("providerKey", out var provider) || provider.GetString() != ProviderKey || view.TryGetProperty("linkSource", out _)) continue;
                definitions.Add(new(module.ModuleId, view.GetProperty("key").GetString()!, Read(view)));
            }
        }
        if (definitions.Select(definition => definition.ViewKey).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new JsonException("Duplicate relation tree key.");
        return definitions;
    }
}
