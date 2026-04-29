using ICP.Platform.Persistence.Composition;

namespace ICP.Platform.Persistence.DevSync.Coordination;

/// <summary>
/// 模型 authoring 真源清单检查结果；缺失模型会阻断开发态结构同步和后续 Host 启动。
/// </summary>
public sealed record ModelSourceInventoryResult(
    bool Success,
    IReadOnlyList<string> MissingModels,
    IReadOnlyList<string> LoadedModels);

/// <summary>
/// 校验 catalog 中受管模型是否都存在于模块根目录的 <c>metadata/models</c> authoring 真源下。
/// 该检查不会接受 archive、generated 或其他运行时目录中的同名文件作为真源。
/// </summary>
public sealed class ModelSourceInventory
{
    private readonly Func<CancellationToken, Task<ModelSourceInventoryResult>> _checkAsync;

    private ModelSourceInventory(Func<CancellationToken, Task<ModelSourceInventoryResult>> checkAsync)
    {
        _checkAsync = checkAsync;
    }

    /// <summary>
    /// 基于当前 catalog 结果视图建立真源清单检查，确认每个受管模型都有对应的 model.json。
    /// </summary>
    public static ModelSourceInventory Create(DevelopmentSchemaSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var requiredModels = PlatformPersistenceModelCatalog.GetDefinitions()
            .Select(static definition => definition.Name)
            .ToArray();

        return CreateForTests(requiredModels, options.RepositoryRoot);
    }

    /// <summary>
    /// 为协调器测试构造固定成功结果，避免测试依赖当前仓库真实模型集合。
    /// </summary>
    public static ModelSourceInventory Success(IReadOnlyList<string> loadedModels)
    {
        ArgumentNullException.ThrowIfNull(loadedModels);

        return new ModelSourceInventory(_ =>
            Task.FromResult(new ModelSourceInventoryResult(true, [], loadedModels.ToArray())));
    }

    /// <summary>
    /// 为测试传入受管模型清单和临时仓库根，复用生产路径的文件发现规则。
    /// </summary>
    public static ModelSourceInventory CreateForTests(IReadOnlyList<string> requiredModels, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(requiredModels);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        return new ModelSourceInventory(cancellationToken =>
            Task.FromResult(Check(requiredModels, repositoryRoot, cancellationToken)));
    }

    /// <summary>
    /// 执行真源清单检查；失败信号只描述缺失模型，不尝试在运行时回退到其他定义来源。
    /// </summary>
    public Task<ModelSourceInventoryResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        return _checkAsync(cancellationToken);
    }

    private static ModelSourceInventoryResult Check(
        IReadOnlyList<string> requiredModels,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var modulesRoot = Path.Combine(Path.GetFullPath(repositoryRoot), "modules");
        var modelNames = Directory.Exists(modulesRoot)
            ? Directory.EnumerateDirectories(modulesRoot)
                .Select(static moduleRoot => Path.Combine(moduleRoot, "metadata", "models"))
                .Where(Directory.Exists)
                .SelectMany(static modelDirectory => Directory.EnumerateFiles(modelDirectory, "*.model.json", SearchOption.TopDirectoryOnly))
                .Select(static path => Path.GetFileName(path))
                .Where(static fileName => fileName.EndsWith(".model.json", StringComparison.Ordinal))
                .Select(static fileName => fileName[..^".model.json".Length])
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        cancellationToken.ThrowIfCancellationRequested();

        var loaded = requiredModels
            .Where(modelNames.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var missing = requiredModels
            .Where(model => !modelNames.Contains(model))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ModelSourceInventoryResult(missing.Length == 0, missing, loaded);
    }
}
