using ICP.Platform.Kernel.Abstractions;

namespace ICP.Platform.Kernel.ModuleLoader;

/// <summary>
/// 模块注册表
/// </summary>
public class ModuleRegistry
{
    private readonly Dictionary<string, IModule> _modules = new();
    private readonly Dictionary<string, List<MenuDescriptor>> _menus = new();
    private readonly Dictionary<string, List<ConfigDescriptor>> _configs = new();
    private readonly Dictionary<string, List<MigrationDescriptor>> _migrations = new();

    /// <summary>
    /// 注册模块
    /// </summary>
    public void RegisterModule(IModule module)
    {
        if (_modules.ContainsKey(module.ModuleId))
        {
            throw new InvalidOperationException($"模块 {module.ModuleId} 已注册");
        }

        _modules[module.ModuleId] = module;

        // 收集菜单声明
        if (module is IMenuProvider menuProvider)
        {
            _menus[module.ModuleId] = menuProvider.GetMenus().ToList();
        }

        // 收集配置声明
        if (module is IConfigProvider configProvider)
        {
            _configs[module.ModuleId] = configProvider.GetConfigSchema().ToList();
        }

        // 收集迁移声明
        if (module is IMigrationProvider migrationProvider)
        {
            _migrations[module.ModuleId] = migrationProvider.GetMigrations().ToList();
        }
    }

    /// <summary>
    /// 获取所有模块
    /// </summary>
    public IReadOnlyDictionary<string, IModule> GetAllModules() => _modules;

    /// <summary>
    /// 获取所有菜单
    /// </summary>
    public IReadOnlyList<MenuDescriptor> GetAllMenus()
    {
        return _menus.Values.SelectMany(m => m).ToList();
    }

    /// <summary>
    /// 获取所有配置
    /// </summary>
    public IReadOnlyList<ConfigDescriptor> GetAllConfigs()
    {
        return _configs.Values.SelectMany(c => c).ToList();
    }

    /// <summary>
    /// 获取所有迁移任务
    /// </summary>
    public IReadOnlyList<MigrationDescriptor> GetAllMigrations()
    {
        return _migrations.Values.SelectMany(m => m).ToList();
    }

    /// <summary>
    /// 获取指定模块
    /// </summary>
    public IModule? GetModule(string moduleId)
    {
        _modules.TryGetValue(moduleId, out var module);
        return module;
    }
}
