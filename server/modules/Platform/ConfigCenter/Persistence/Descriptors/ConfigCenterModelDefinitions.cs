using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public static class ConfigCenterModelDefinitions
{
    private static readonly Lazy<IReadOnlyList<ModelDefinition>> Definitions = new(
        static () => new ConfigCenterModelMetadataProvider().GetDefinitions());

    /// <summary>
    /// 配置中心运行时模型定义。
    /// 该 legacy facade 只转发 metadata provider，避免重新形成手写定义真源。
    /// </summary>
    public static IReadOnlyList<ModelDefinition> All => Definitions.Value;

    /// <summary>
    /// 兼容既有调用方的单模型入口；真实定义仍只来自 metadata provider 聚合结果。
    /// </summary>
    public static ModelDefinition ConfigItem => All.Single(static model => model.Name == "ConfigItem");
}
