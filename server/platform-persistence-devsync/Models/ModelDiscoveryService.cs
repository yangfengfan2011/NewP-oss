namespace ICP.Platform.Persistence.DevSync.Models;

/// <summary>
/// 发现模块目录下的模型 authoring 文档，并保持稳定排序。
/// 该服务只负责扫描与加载；校验、投影和失败阻断由后续内核阶段处理。
/// </summary>
public sealed class ModelDiscoveryService
{
    private readonly SingleRootModelDocumentLoader loader;

    /// <summary>
    /// 创建模型发现服务；调用方注入 loader 以便测试复用同一发现规则。
    /// </summary>
    /// <param name="loader">模型文档加载器。</param>
    public ModelDiscoveryService(SingleRootModelDocumentLoader loader)
    {
        this.loader = loader;
    }

    /// <summary>
    /// 从仓库根的 <c>modules/*/metadata/models</c> 发现全部模型文档；缺少 modules 目录时返回空集合。
    /// </summary>
    /// <param name="repositoryRoot">仓库根目录。</param>
    /// <returns>按模块和模型名稳定排序的发现结果。</returns>
    public IReadOnlyList<DiscoveredModelDocument> DiscoverAll(string repositoryRoot)
    {
        var modulesRoot = Path.Combine(Path.GetFullPath(repositoryRoot), "modules");
        if (!Directory.Exists(modulesRoot))
        {
            return [];
        }

        var discovered = Directory.EnumerateFiles(modulesRoot, "*.model.json", SearchOption.AllDirectories)
            .Select(path =>
            {
                var document = loader.LoadFromFile(path);
                return new DiscoveredModelDocument
                {
                    ModuleId = document.ModuleId,
                    ModelName = document.Model.Name,
                    FilePath = Path.GetFullPath(path),
                    Document = document
                };
            })
            .OrderBy(item => item.ModuleId, StringComparer.Ordinal)
            .ThenBy(item => item.ModelName, StringComparer.Ordinal)
            .ToArray();

        return discovered;
    }
}
