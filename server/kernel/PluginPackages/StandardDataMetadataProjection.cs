using System.Text.Json;
using System.Text.Json.Nodes;

namespace ICP.Platform.Kernel.PluginPackages;

/// <summary>standard-data 候选目录与运行时页面共享的纯元数据投影；不声明第二份业务权限。</summary>
public static class StandardDataMetadataProjection
{
    /// <summary>读取包内现有元数据，路径仅作为投影结果的相对文件键。</summary>
    public static IReadOnlyDictionary<string, JsonObject> ReadDocuments(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            path => JsonNode.Parse(File.ReadAllText(path))!.AsObject(), StringComparer.Ordinal)
        : new Dictionary<string, JsonObject>(StringComparer.Ordinal);

    /// <summary>从已确认 Blueprint 生成页面事实，在候选阶段即可展开完整目录；Model 仍由原投影 owner 生成。</summary>
    public static IReadOnlyDictionary<string, JsonObject> Project(string moduleId, JsonObject blueprint, JsonObject projection,
        IReadOnlyDictionary<string, JsonObject> sourceDocuments)
    {
        var documents = sourceDocuments.ToDictionary(item => item.Key, item => (JsonObject)item.Value.DeepClone(), StringComparer.Ordinal);
        var standardData = blueprint["standardData"]?.AsObject()
            ?? throw new JsonException($"Standard data Blueprint for '{moduleId}' is missing standardData.");
        var resources = standardData["resources"]?.AsArray()
            ?? throw new JsonException($"Standard data Blueprint for '{moduleId}' is missing resources.");
        var projectedResources = projection["resources"]?.AsArray()
            ?? throw new JsonException($"Standard data projection for '{moduleId}' is missing resources.");
        var navigation = blueprint["navigation"]?.AsObject()
            ?? throw new JsonException($"Standard data Blueprint for '{moduleId}' is missing navigation.");
        var moduleSlug = RequiredString(projection, "moduleSlug", moduleId);
        var navigationItems = new List<object>
        {
            new
            {
                key = moduleSlug,
                titleKey = RequiredString(navigation, "group", moduleId),
                icon = "database",
                order = navigation["order"]?.GetValue<int>() ?? 0
            }
        };

        foreach (var resourceNode in resources)
        {
            var resource = resourceNode?.AsObject()
                ?? throw new JsonException($"Standard data Blueprint for '{moduleId}' contains an invalid resource.");
            var resourceKey = RequiredString(resource, "key", moduleId);
            var resourceSlug = ToRouteToken(resourceKey);
            var resourceProjection = projectedResources
                .Select(static item => item?.AsObject())
                .SingleOrDefault(item => item is not null
                    && string.Equals(item["resourceKey"]?.GetValue<string>(), resourceKey, StringComparison.Ordinal))
                ?? throw new JsonException($"Standard data resource '{moduleId}.{resourceKey}' has no projection.");
            var pageRoute = RequiredString(resourceProjection, "pageRoute", moduleId);
            var declaredDetailViewKey = OptionalString(resource, "detailViewKey", moduleId);
            var projectedDetailViewKey = OptionalString(resourceProjection, "detailViewKey", moduleId);
            if (!string.Equals(declaredDetailViewKey, projectedDetailViewKey, StringComparison.Ordinal))
            {
                throw new JsonException(
                    $"Standard data resource '{moduleId}.{resourceKey}' has inconsistent detailViewKey projection.");
            }

            if (declaredDetailViewKey is not null)
            {
                BindDetail(documents, moduleId, resourceKey, declaredDetailViewKey);
            }

            var listViewKey = $"{moduleSlug}.{resourceSlug}.list";
            var fields = resource["fields"]?.AsArray()
                ?? throw new JsonException($"Standard data resource '{moduleId}.{resourceKey}' is missing fields.");
            var hasEnabledField = fields
                .Select(static fieldNode => fieldNode?.AsObject())
                .Any(static field => string.Equals(
                    field?["name"]?.GetValue<string>(),
                    "IsEnabled",
                    StringComparison.Ordinal));

            navigationItems.Add(new
            {
                key = $"{moduleSlug}.{resourceSlug}",
                titleKey = RequiredString(resource, "displayName", moduleId),
                icon = "table",
                parentKey = moduleSlug,
                path = pageRoute,
                order = navigation["order"]?.GetValue<int>() ?? 0,
                entryType = "standard-list",
                targetKey = listViewKey,
                featureAuthorization = true
            });

            var columns = fields.Select(fieldNode =>
            {
                var field = fieldNode?.AsObject()
                    ?? throw new JsonException($"Standard data resource '{moduleId}.{resourceKey}' contains an invalid field.");
                var fieldName = RequiredString(field, "name", moduleId);
                return new
                {
                    key = ToRouteToken(fieldName),
                    labelKey = fieldName,
                    source = "model",
                    field = fieldName,
                    sortable = true
                };
            }).ToArray();
            var rowActions = new List<object>();
            if (declaredDetailViewKey is not null)
            {
                rowActions.Add(new { key = "view", labelKey = "Action_View", standardAction = "view", variant = (string?)null });
            }

            rowActions.Add(new { key = "edit", labelKey = "Action_Edit", standardAction = "edit", variant = (string?)null });
            if (hasEnabledField)
            {
                // standard-data.v1 没有删除策略声明；这里只投影描述符已支持的停启能力，
                // 避免页面暴露一个必然被 DeletePolicy.None 拒绝的删除动作。
                rowActions.Add(new
                {
                    key = "disable",
                    labelKey = "Action_Disable",
                    standardAction = "disable",
                    variant = "danger",
                    visibleWhen = new { field = "IsEnabled", @operator = "equals", value = true }
                });
                rowActions.Add(new
                {
                    key = "enable",
                    labelKey = "Action_Enable",
                    standardAction = "enable",
                    variant = (string?)null,
                    visibleWhen = new { field = "IsEnabled", @operator = "equals", value = false }
                });
            }

            var listView = new JsonObject
            {
                ["key"] = listViewKey,
                ["dicKey"] = $"{resourceKey}ListView",
                ["titleKey"] = RequiredString(resource, "displayName", moduleId),
                ["mode"] = "list",
                ["model"] = resourceKey,
                ["resourceKey"] = $"{moduleId}.{resourceKey}",
                ["editorMode"] = "drawer",
                // 通用资源授权端点消费每个CRUD资源的功能声明；声明不增加页面行操作。
                ["featureActions"] = JsonSerializer.SerializeToNode(new[]
                {
                    new { key = "authorize", titleKey = "Authorize", behaviorKey = $"{moduleId}.{resourceKey}.authorize" }
                }),
                ["columns"] = JsonSerializer.SerializeToNode(columns),
                ["pageActions"] = JsonSerializer.SerializeToNode(new object[]
                {
                    new { key = "create", labelKey = "Action_Create", standardAction = "create", variant = "primary" },
                    new { key = "refresh", labelKey = "Action_Refresh", standardAction = "refresh", variant = (string?)null }
                }),
                ["rowActions"] = JsonSerializer.SerializeToNode(rowActions)
            };
            if (declaredDetailViewKey is not null)
            {
                listView["detailViewKey"] = declaredDetailViewKey;
                navigationItems.Add(new { key = $"{moduleSlug}.{resourceSlug}.detail", path = pageRoute.TrimEnd('/') + "/:id",
                    routeOnly = true, entryType = "standard-crud-detail", targetKey = declaredDetailViewKey,
                    featureOwner = "navigation:" + moduleSlug + "." + resourceSlug });
            }

            documents[$"views/{resourceKey}List.view.json"] = new JsonObject
                {
                    ["schemaVersion"] = "1.0",
                    ["type"] = "view",
                    ["view"] = listView
                };

        }

        documents["navigation/standard-data.nav.json"] = JsonSerializer.SerializeToNode(new
            {
                schemaVersion = "1.0",
                type = "nav",
                navigation = new { items = navigationItems }
            })!.AsObject();
        return documents;
    }

    // 显式详情保持包内结构，只补入同资源与导航位置；缺失或错模型在候选阶段拒绝。
    private static void BindDetail(Dictionary<string, JsonObject> documents, string moduleId, string resourceKey, string viewKey)
    {
        var matches = documents.Values.Select(document => document["view"] as JsonObject)
            .Where(view => view is not null && OptionalString(view, "key", moduleId) == viewKey).ToArray();
        if (matches.Length != 1 || OptionalString(matches[0]!, "mode", moduleId) != "detail"
            || OptionalString(matches[0]!, "model", moduleId) != resourceKey)
            throw new JsonException($"Standard data resource '{moduleId}.{resourceKey}' detailViewKey '{viewKey}' must resolve to exactly one matching package Detail View.");
        matches[0]!["resourceKey"] = $"{moduleId}.{resourceKey}";
        RemoveLegacyPermissions(matches[0]!);
    }

    // 已迁移详情及动作由同一目录授权，旧权限引用不能形成额外的不可授予条件。
    private static void RemoveLegacyPermissions(JsonNode node)
    {
        if (node is JsonObject value)
        {
            value.Remove("permissions");
            foreach (var child in value.ToArray()) if (child.Value is not null) RemoveLegacyPermissions(child.Value);
        }
        else if (node is JsonArray items) foreach (var child in items) if (child is not null) RemoveLegacyPermissions(child);
    }

    // 读取必需非空字符串，保持包错误在 staging 内原子失败。
    private static string RequiredString(JsonObject value, string propertyName, string moduleId) =>
        value[propertyName]?.GetValue<string>() is { Length: > 0 } result
            ? result
            : throw new JsonException($"Standard data package '{moduleId}' is missing {propertyName}.");

    // 读取可选非空字符串；显式 null、空值或错误类型均视为损坏的包契约。
    private static string? OptionalString(JsonObject value, string propertyName, string moduleId)
    {
        if (!value.TryGetPropertyValue(propertyName, out var node))
        {
            return null;
        }

        if (node is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var result)
            && !string.IsNullOrWhiteSpace(result))
        {
            return result;
        }

        throw new JsonException($"Standard data package '{moduleId}' has an invalid {propertyName}.");
    }

    // 保持现有 standard-data 路由片段算法，避免授权迁移改变页面地址。
    private static string ToRouteToken(string value) =>
        string.Concat(value.Select((character, index) =>
            char.IsUpper(character) && index > 0 ? $"-{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

}
