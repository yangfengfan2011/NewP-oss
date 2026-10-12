using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 返回当前由平台基础迁移流承载的内建模型。
/// </summary>
public static class PlatformFoundationModelSource
{
    /// <summary>
    /// 平台基础流只承载尚无独立 migration bundle 的 Authorization 与 ConfigCenter。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
        AdaptOwner(new AuthorizationModelMetadataProvider().GetDefinitions(), "Authorization")
            .Concat(AdaptOwner(new ConfigCenterModelMetadataProvider().GetDefinitions(), "ConfigCenter"))
            .ToArray();

    /// <summary>从显式隔离根读取同一基础流 authoring，供 finalize 重放而不消费调用进程的源码路径。</summary>
    public static IReadOnlyList<ModelDefinition> GetDefinitions(string repositoryRoot)
    {
        var map = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json"));
        var loader = new JsonModelMetadataDocumentLoader();
        var validator = new ModelMetadataValidator();
        var projector = new ModelMetadataProjector();
        return new[] { "Authorization", "ConfigCenter" }.SelectMany(owner =>
            Directory.EnumerateFiles(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(repositoryRoot, owner), "metadata", "models"), "*.model.json")
                .Order(StringComparer.Ordinal).Select(path =>
                {
                    var document = loader.LoadFromFile(path);
                    validator.Validate(document, map);
                    return projector.Project(document, map, "SqlServer") with { OwnerModuleId = owner, SchemaName = "dbo" };
                })).ToArray();
    }

    // 基础流只适配拥有者；全局模型引用独立于该迁移流归属。
    private static IEnumerable<ModelDefinition> AdaptOwner(
        IEnumerable<ModelDefinition> definitions,
        string ownerModuleId) =>
        definitions.Select(definition => definition with
        {
            OwnerModuleId = ownerModuleId
        });
}
