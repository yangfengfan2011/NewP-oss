using System.Text.Json;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Crud.Planning;

/// <summary>
/// 从活动 Canonical Module Plan 引用的 standard-data 包生成标准 CRUD 描述符。
/// 包内容已由 Plugin Platform 预检；这里仍约束路径与投影对应关系，避免 Host 产生第二套资源事实。
/// </summary>
public sealed class StandardDataCrudDescriptorProjector
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<StandardCrudDescriptor> Project(CanonicalModulePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Modules
            .Where(static module => string.Equals(
                module.FrontendBindingMode,
                "standard-data",
                StringComparison.Ordinal))
            .SelectMany(ProjectModule)
            .OrderBy(static descriptor => descriptor.ResourceKey, StringComparer.Ordinal)
            .ToArray();
    }

    // Required authoring 先收敛为同一字段集合，再同时投影规范策略和唯一错误展示覆盖。
    private IEnumerable<StandardCrudDescriptor> ProjectModule(CanonicalModulePlanModule module)
    {
        var manifest = ReadJson<StandardPluginPackageManifest>(module.PackageRoot, "plugin.json");
        var blueprint = ReadJson<StandardPluginBlueprint>(module.PackageRoot, manifest.Artifacts.Blueprint);
        var projection = ReadJson<StandardPluginProjection>(module.PackageRoot, manifest.Artifacts.StandardProjection);

        if (!string.Equals(blueprint.ModuleId, module.ModuleId, StringComparison.Ordinal)
            || !string.Equals(blueprint.Version, module.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Standard data package identity does not match active module '{module.ModuleId}@{module.Version}'.");
        }

        foreach (var resource in blueprint.StandardData.Resources.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            var resourceProjection = projection.Resources.SingleOrDefault(item =>
                string.Equals(item.ResourceKey, resource.Key, StringComparison.Ordinal))
                ?? throw new InvalidDataException(
                    $"Standard data resource '{module.ModuleId}.{resource.Key}' has no projection.");
            var resourceKey = $"{module.ModuleId}.{resource.Key}";
            var fields = resource.Fields.Select(static field => field.Name).ToArray();
            var listFields = new[] { "EID" }.Concat(fields).ToArray();
            var detailFields = listFields.Concat(["CreatedAt", "UpdatedAt"]).ToArray();
            var requiredFields = resource.Fields
                .Where(static field => field.Required)
                .ToArray();

            yield return new StandardCrudDescriptor
            {
                ResourceKey = resourceKey,
                ModuleId = module.ModuleId,
                ModelName = resource.Key,
                Route = resourceProjection.ApiRoute.Trim('/'),
                IdField = "EID",
                DisplayField = resource.DisplayField,
                EnabledField = fields.Contains("IsEnabled", StringComparer.Ordinal) ? "IsEnabled" : null,
                FieldSet = new StandardCrudFieldSet(
                    listFields,
                    detailFields,
                    fields,
                    fields),
                Authorization = StandardCrudAuthorization.ByConvention(resourceKey),
                OptionLabelFields = [resource.DisplayField],
                FieldPolicies = requiredFields
                    .Select(static field => new StandardCrudFieldPolicy
                    {
                        Field = field.Name,
                        StorageRequired = true,
                        CreateInputRequired = true,
                        UpdateNonEmpty = true,
                        ValueOwner = StandardCrudFieldValueOwner.Caller
                    })
                    .ToArray(),
                RequiredFields = requiredFields
                    .Select(field => new StandardCrudRequiredField
                    {
                        Field = field.Name,
                        Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                        ErrorCode = $"{resourceKey}.{field.Name}Required",
                        Message = $"{resource.DisplayName}的{field.Name}不能为空。",
                        ReasonKey = $"{resourceKey}.{field.Name}.required"
                    })
                    .ToArray(),
                UniqueConstraints = resource.Fields
                    .Where(static field => field.Unique)
                    .Select(field => new StandardCrudUniqueConstraint
                    {
                        Key = field.Name,
                        Fields = [field.Name],
                        ErrorCode = $"{resourceKey}.{field.Name}Duplicated",
                        Message = $"{resource.DisplayName}的{field.Name}必须唯一。",
                        ReasonKey = $"{resourceKey}.{field.Name}.duplicated"
                    })
                    .ToArray()
            };
        }
    }

    private T ReadJson<T>(string root, string relativePath)
    {
        var resolvedRoot = Path.GetFullPath(root);
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Standard data package path is missing or unsafe: {relativePath}");
        }

        var path = Path.GetFullPath(Path.Combine(resolvedRoot, relativePath));
        if (!path.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            throw new InvalidDataException($"Standard data package file is missing or unsafe: {relativePath}");
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _jsonOptions)
            ?? throw new JsonException($"Standard data package file '{relativePath}' deserialized to null.");
    }
}
