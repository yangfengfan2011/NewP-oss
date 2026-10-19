using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>正式Link列表读取声明只从实际owner可信view来源投影，不从执行名称猜测用途。</summary>
public static class LinkQueryReadMetadata
{
    /// <summary>冻结同一Link模型及查询资源的真实view/read集合；声明缺失不能回退其他view或树声明。</summary>
    public static IReadOnlyList<ActionDataAccessRequirement> Project(CanonicalModulePlan plan, string moduleId, string modelKey, string resourceKey)
    {
        var owner = plan.Modules.Single(module => module.ModuleId == moduleId);
        var snapshot = owner.Provenance.GetAuthenticatedBuiltInSnapshot(owner);
        if (snapshot is null) return [];
        var viewKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in snapshot.EnumerateFiles("metadata/views").Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var document = JsonDocument.Parse(snapshot.ReadFile(path));
            if (document.RootElement.TryGetProperty("view", out var view) && view.TryGetProperty("querySource", out var source)
                && source.GetProperty("kind").GetString() == "link" && source.GetProperty("resourceKey").GetString() == resourceKey
                && source.GetProperty("model").GetString() == modelKey)
                viewKeys.Add(view.GetProperty("key").GetString()!);
        }
        return viewKeys.Select(key => plan.ActionDataAccessCatalog.SingleOrDefault(item => item.ModuleId == moduleId && item.ViewKey == key
            && item.Surface == "read" && item.ActionKey == "")
            ?? throw new InvalidOperationException("LINK_QUERY_VIEW_DATA_EXECUTION_MISSING")).ToArray();
    }
}
