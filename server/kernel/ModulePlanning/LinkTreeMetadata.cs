using System.Text.Json;
using System.Text.RegularExpressions;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>单一 Entity 的多父 Link 树；静态配置不包含 SQL 或请求表达式。</summary>
public sealed record LinkTreeSource(string ResourceKey, string NodeType, string ModelKey, string LabelColumn, string OrderField, string RootPolicyKey,
    IReadOnlyList<string>? RootContextMenuKeys = null);
public sealed record LinkTreeDefinition(string ModuleId, string ViewKey, LinkTreeSource Source);

/// <summary>从签发快照读取唯一 Link 树来源；模块包装不能另建查询目录。</summary>
public static class LinkTreeMetadata
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    /// <summary>拒绝来源混用、非法字段及不能与唯一节点类型对应的配置。</summary>
    public static LinkTreeSource Read(JsonElement view)
    {
        if (!view.TryGetProperty("mode", out var mode) || mode.GetString() != "tree") throw new JsonException("Link source requires a Tree View.");
        if (view.TryGetProperty("relationSource", out _)) throw new JsonException("Tree sources are mutually exclusive.");
        var source = view.GetProperty("linkSource").Deserialize<LinkTreeSource>(Options) ?? throw new JsonException("Missing link tree source.");
        var types = view.GetProperty("nodeTypes").EnumerateArray().Select(node => node.GetProperty("key").GetString()).ToArray();
        if (types.Length != 1 || types[0] != source.NodeType) throw new JsonException("Link tree requires its single traversal node type.");
        foreach (var name in new[] { source.NodeType, source.ModelKey, source.LabelColumn, source.OrderField })
            if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                throw new JsonException("Invalid link tree identifier.");
        if (string.IsNullOrWhiteSpace(source.ResourceKey) || string.IsNullOrWhiteSpace(source.RootPolicyKey)) throw new JsonException("Missing link tree resource or root policy.");
        if (source.RootContextMenuKeys is { } keys)
        {
            var node = view.GetProperty("nodeTypes")[0];
            var menus = node.TryGetProperty("contextMenu", out var menu) ? menu.EnumerateArray().Select(item => item.GetProperty("key").GetString()).ToArray() : [];
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(key => !menus.Contains(key, StringComparer.Ordinal)))
                throw new JsonException("Invalid root menu references.");
        }
        return source;
    }

    /// <summary>所有声明 linkSource 的受控树共享目录，包含执行显式进入命令的模块包装。</summary>
    public static IReadOnlyList<LinkTreeDefinition> Project(CanonicalModulePlan plan)
    {
        var result = new List<LinkTreeDefinition>();
        foreach (var module in plan.Modules)
        {
            var snapshot = module.Provenance.GetAuthenticatedBuiltInSnapshot(module);
            if (snapshot is null) continue;
            foreach (var path in snapshot.EnumerateFiles("metadata/views").Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                using var document = JsonDocument.Parse(snapshot.ReadFile(path));
                if (document.RootElement.TryGetProperty("view", out var view) && view.TryGetProperty("linkSource", out _))
                    result.Add(new(module.ModuleId, view.GetProperty("key").GetString()!, Read(view)));
            }
        }
        if (result.Select(tree => tree.ViewKey).Distinct(StringComparer.Ordinal).Count() != result.Count) throw new JsonException("Duplicate link tree key.");
        return result;
    }
}
