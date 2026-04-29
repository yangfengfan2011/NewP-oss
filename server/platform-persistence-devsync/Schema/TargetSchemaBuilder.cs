using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Persistence.DevSync.Models;

namespace ICP.Platform.Persistence.DevSync.Schema;

/// <summary>
/// 将模型 authoring 文档投影为目标数据库结构。
/// 该 builder 复用 modeling 投影链，避免 devsync 复制字段、索引或类型规则；校验失败会直接向上暴露。
/// </summary>
public sealed class TargetSchemaBuilder
{
    private readonly string _repositoryRoot;
    private readonly JsonStorageTypeMapLoader _typeMapLoader = new();
    private readonly ModelMetadataValidator _validator = new();
    private readonly ModelMetadataProjector _projector = new();

    /// <summary>
    /// 创建目标 schema builder。
    /// </summary>
    /// <param name="repositoryRoot">仓库根目录，用于定位共享 storage type map。</param>
    public TargetSchemaBuilder(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

    /// <summary>
    /// 从已发现模型生成目标 schema；模型校验或类型映射失败即表示 authoring 输入不可用于同步。
    /// </summary>
    /// <param name="models">已发现的模型文档集合。</param>
    /// <returns>目标数据库结构视图。</returns>
    public TargetSchema Build(IReadOnlyList<DiscoveredModelDocument> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        var storageTypeMapPath = Path.Combine(
            _repositoryRoot,
            "server",
            "platform-modeling",
            "metadata",
            "storage-types.json");
        var typeMap = _typeMapLoader.LoadFromFile(storageTypeMapPath);

        var tables = models.Select(BuildTable).ToList();
        return new TargetSchema(tables);

        TargetTableSchema BuildTable(DiscoveredModelDocument model)
        {
            _validator.Validate(model.Document, typeMap);

            // 目标 schema 必须与当前建模投影链保持同源，避免工具侧复制一份固定字段/索引规则。
            var definition = _projector.Project(model.Document, typeMap, "SqlServer");

            return new TargetTableSchema(
                model.ModuleId,
                definition.Name,
                definition.TableName,
                model.Document.Model.HistoryName,
                definition.Fields.Select(field => new TargetColumnSchema(
                    field.Name,
                    field.StoreName,
                    model.Document.Model.Fields.FirstOrDefault(item => string.Equals(item.Name, field.Name, StringComparison.Ordinal))?.HistoryName,
                    field.StoreType,
                    field.Required)).ToList(),
                definition.Indexes.Select(index => new TargetIndexSchema(
                    index.Name,
                    index.Fields,
                    index.IsUnique)).ToList());
        }
    }
}
