using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Tools.DevSchemaSync.Models;

namespace ICP.Platform.Tools.DevSchemaSync.Schema;

public sealed class TargetSchemaBuilder
{
    private readonly string _repositoryRoot;
    private readonly JsonStorageTypeMapLoader _typeMapLoader = new();
    private readonly ModelMetadataValidator _validator = new();
    private readonly ModelMetadataProjector _projector = new();

    public TargetSchemaBuilder(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

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
