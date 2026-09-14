using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>可提交的轻量功能声明；Key 属于模块声明，行为绑定不拥有授权身份。</summary>
public sealed record FeatureDeclaration(string ModuleId, string Key, string Kind, string Title,
    string? ParentKey, string BehaviorKey, int Order = 0);

/// <summary>完整位置与入口依赖由编译器派生，页面实现不形成授权点。</summary>
public sealed record FeaturePoint(string Position, string ModuleId, string Key, string Kind, string Title,
    string? ParentPosition, string BehaviorKey, IReadOnlyList<string> EntryDependencies, int Order);

/// <summary>以确定规则将模块声明编译为全有或全无的功能目录。</summary>
public static class FeatureCatalogCompiler
{
    /// <summary>读取自定义页面的静态声明；缺少声明表示模块尚未迁移，不从旧 Permission 推断。</summary>
    public static IReadOnlyList<FeatureDeclaration> Read(JsonElement capsule, string moduleId)
    {
        if (!capsule.TryGetProperty("features", out var features)) return [];
        if (features.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("features 必须是数组。");
        return features.EnumerateArray().Select(node => new FeatureDeclaration(moduleId,
            node.GetProperty("key").GetString()!, node.GetProperty("kind").GetString()!,
            node.GetProperty("title").GetString()!,
            node.TryGetProperty("parentKey", out var parent) ? parent.GetString() : null,
            node.GetProperty("behaviorKey").GetString()!,
            node.TryGetProperty("order", out var order) ? order.GetInt32() : 0)).ToArray();
    }

    /// <summary>拒绝重复、循环、缺少入口及必需行为声明；按位置输出确定快照。</summary>
    public static IReadOnlyList<FeaturePoint> Compile(IEnumerable<FeatureDeclaration> declarations)
    {
        var nodes = new Dictionary<(string Module, string Key), FeatureDeclaration>();
        foreach (var node in declarations)
        {
            if (string.IsNullOrWhiteSpace(node.ModuleId) || string.IsNullOrWhiteSpace(node.Key)
                || string.IsNullOrWhiteSpace(node.Title) || string.IsNullOrWhiteSpace(node.BehaviorKey)
                || node.Kind is not ("navigation" or "tab" or "action")
                || !nodes.TryAdd((node.ModuleId, node.Key), node))
                throw new InvalidOperationException("功能声明存在重复身份、非法种类或缺少必需绑定。");
        }
        var compiled = new Dictionary<(string Module, string Key), FeaturePoint>();
        var visiting = new HashSet<(string Module, string Key)>();
        // 入口链只遍历一次；活动目录构建不访问角色分配。
        FeaturePoint Visit((string Module, string Key) identity)
        {
            if (compiled.TryGetValue(identity, out var result)) return result;
            if (!nodes.TryGetValue(identity, out var node) || !visiting.Add(identity))
                throw new InvalidOperationException("功能声明存在缺失引用或循环入口。");
            var parent = node.ParentKey is null ? null : Visit((node.ModuleId, node.ParentKey));
            if (parent?.Kind == "action" || (parent is null && node.Kind != "navigation"))
                throw new InvalidOperationException("Tab 和动作必须声明所属入口，动作不能承载子入口。");
            var position = $"{parent?.Position ?? Uri.EscapeDataString(node.ModuleId)}/{node.Kind}:{Uri.EscapeDataString(node.Key)}";
            result = new(position, node.ModuleId, node.Key, node.Kind, node.Title, parent?.Position,
                node.BehaviorKey, parent is null ? [] : [.. parent.EntryDependencies, parent.Position], node.Order);
            compiled.Add(identity, result);
            visiting.Remove(identity);
            return result;
        }
        foreach (var identity in nodes.Keys) Visit(identity);
        return compiled.Values.OrderBy(point => point.Position, StringComparer.Ordinal).ToArray();
    }

    /// <summary>完整集合必须只含活动位置且自行满足本分支入口依赖。</summary>
    public static void ValidateSelection(IReadOnlyList<FeaturePoint> catalog, IReadOnlyCollection<string> positions)
    {
        var selected = positions.ToHashSet(StringComparer.Ordinal);
        var known = catalog.ToDictionary(point => point.Position, StringComparer.Ordinal);
        foreach (var position in selected)
        {
            if (string.IsNullOrWhiteSpace(position) || !known.TryGetValue(position, out var point)) throw new InvalidOperationException("授权集合包含未知或失效功能。");
            if (point.EntryDependencies.Any(dependency => !selected.Contains(dependency)))
                throw new InvalidOperationException($"功能 {point.Title} 缺少本分支入口授权。");
        }
    }
}
