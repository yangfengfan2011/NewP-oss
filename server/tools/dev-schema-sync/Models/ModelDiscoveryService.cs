namespace ICP.Platform.Tools.DevSchemaSync.Models;

// 模型发现只负责全量扫描和排序，不做额外解释，方便后续把发现结果交给独立内核处理。
public sealed class ModelDiscoveryService
{
    private readonly SingleRootModelDocumentLoader loader;

    public ModelDiscoveryService(SingleRootModelDocumentLoader loader)
    {
        this.loader = loader;
    }

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
