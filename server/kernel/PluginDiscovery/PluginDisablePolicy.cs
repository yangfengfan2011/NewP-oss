namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 插件禁用策略
/// </summary>
public class PluginDisablePolicy
{
    private readonly HashSet<string> _disabledPluginIds;

    public PluginDisablePolicy(IEnumerable<string>? disabledPluginIds = null)
    {
        _disabledPluginIds = new HashSet<string>(
            disabledPluginIds ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsDisabled(string moduleId)
    {
        return _disabledPluginIds.Contains(moduleId);
    }
}
