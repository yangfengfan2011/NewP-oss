using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>独立数据动作声明；停用保留身份、引用与读取依赖。</summary>
public sealed record DataActionDeclaration(string ModuleId, string Key, string Title, string BehaviorKey,
    bool Enabled = true, bool RequiresRead = false);

/// <summary>从正式 Capsule 编译唯一的数据动作目录。</summary>
public static class DataActionCatalogCompiler
{
    /// <summary>读取模块拥有的动作；未声明开关时默认启用。</summary>
    public static IReadOnlyList<DataActionDeclaration> Read(JsonElement capsule, string moduleId)
    {
        if (!capsule.TryGetProperty("dataActions", out var actions)) return [];
        return actions.EnumerateArray().Select(action => new DataActionDeclaration(moduleId,
            action.GetProperty("key").GetString()!, action.GetProperty("title").GetString()!,
            action.GetProperty("behaviorKey").GetString()!,
            !action.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean(),
            action.TryGetProperty("requiresRead", out var read) && read.GetBoolean())).ToArray();
    }

    /// <summary>校验全局身份和执行绑定；禁用动作仍保留在结果中。</summary>
    public static IReadOnlyList<DataActionDeclaration> Compile(IEnumerable<DataActionDeclaration> declarations)
    {
        var actions = declarations.OrderBy(action => action.Key, StringComparer.Ordinal).ToArray();
        if (actions.Any(action => string.IsNullOrWhiteSpace(action.ModuleId) || string.IsNullOrWhiteSpace(action.Key)
            || string.IsNullOrWhiteSpace(action.Title) || string.IsNullOrWhiteSpace(action.BehaviorKey))
            || actions.GroupBy(action => action.Key, StringComparer.Ordinal).Any(group => group.Count() > 1)
            || actions.GroupBy(action => action.BehaviorKey, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException("DATA_ACTION_CATALOG_INVALID");
        return actions;
    }
}
