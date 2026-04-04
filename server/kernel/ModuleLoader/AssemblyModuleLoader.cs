using System.Reflection;
using ICP.Platform.Kernel.Abstractions;

namespace ICP.Platform.Kernel.ModuleLoader;

/// <summary>
/// 基于程序集的模块加载器
/// </summary>
public class AssemblyModuleLoader : IModuleLoader
{
    private readonly IEnumerable<Assembly> _assemblies;

    public AssemblyModuleLoader(IEnumerable<Assembly> assemblies)
    {
        _assemblies = assemblies;
    }

    public IReadOnlyList<IModule> DiscoverModules()
    {
        var modules = new List<IModule>();

        foreach (var assembly in _assemblies)
        {
            var moduleTypes = assembly.GetTypes()
                .Where(t => typeof(IModule).IsAssignableFrom(t)
                    && !t.IsInterface
                    && !t.IsAbstract);

            foreach (var moduleType in moduleTypes)
            {
                try
                {
                    var module = (IModule?)Activator.CreateInstance(moduleType);
                    if (module != null)
                    {
                        modules.Add(module);
                    }
                }
                catch (Exception ex)
                {
                    // 记录错误但继续加载其他模块
                    Console.WriteLine($"无法实例化模块 {moduleType.FullName}: {ex.Message}");
                }
            }
        }

        return modules;
    }

    public IReadOnlyList<IModule> SortByDependencies(IReadOnlyList<IModule> modules)
    {
        var sorted = new List<IModule>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        foreach (var module in modules)
        {
            Visit(module, modules, sorted, visited, visiting);
        }

        return sorted;
    }

    private void Visit(
        IModule module,
        IReadOnlyList<IModule> allModules,
        List<IModule> sorted,
        HashSet<string> visited,
        HashSet<string> visiting)
    {
        if (visited.Contains(module.ModuleId))
            return;

        if (visiting.Contains(module.ModuleId))
            throw new InvalidOperationException($"检测到循环依赖: {module.ModuleId}");

        visiting.Add(module.ModuleId);

        // 获取依赖
        var dependencies = GetModuleDependencies(module);
        foreach (var depId in dependencies)
        {
            var depModule = allModules.FirstOrDefault(m => m.ModuleId == depId);
            if (depModule != null)
            {
                Visit(depModule, allModules, sorted, visited, visiting);
            }
        }

        visiting.Remove(module.ModuleId);
        visited.Add(module.ModuleId);
        sorted.Add(module);
    }

    private IReadOnlyList<string> GetModuleDependencies(IModule module)
    {
        // 如果模块实现了 IModuleMetadata，使用其依赖信息
        if (module is IModuleMetadata metadata)
        {
            return metadata.Dependencies;
        }
        return Array.Empty<string>();
    }
}
