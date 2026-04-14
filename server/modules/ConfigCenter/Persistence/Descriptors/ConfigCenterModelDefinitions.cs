using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public static class ConfigCenterModelDefinitions
{
    // 这里不再手写模型定义，统一复用模型元数据 provider 的加载与投影链路。
    private static readonly ConfigCenterModelMetadataProvider Provider = new();

    public static readonly ModelDefinition ValueListItem = Provider.GetRequired("ValueListItem");

    public static readonly ModelDefinition ConfigItem = Provider.GetRequired("ConfigItem");

    public static readonly IReadOnlyList<ModelDefinition> All = Provider.GetAll();
}
