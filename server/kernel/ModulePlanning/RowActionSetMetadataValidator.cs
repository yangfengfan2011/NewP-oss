using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>按 Link 端点模型校验两个独立的行动作展示集合。</summary>
internal static class RowActionSetMetadataValidator
{
    /// <summary>动作集合仅引用既有行动作；完整覆盖端点，空数组显式隐藏对应入口。</summary>
    internal static void Validate(JsonElement view, JsonElement[] documents)
    {
        if (view.TryGetProperty("rowActionColumn", out var presentation)
            && (presentation.ValueKind != JsonValueKind.String || presentation.GetString() is not ("default" or "menu")))
            throw new JsonException("Invalid rowActionColumn.");
        if (!view.TryGetProperty("rowActionSets", out var sets)) return;
        if (sets.ValueKind != JsonValueKind.Object || view.TryGetProperty("rowContextMenu", out _)
            || !view.TryGetProperty("querySource", out var source) || source.ValueKind != JsonValueKind.Object
            || !source.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "link"
            || !source.TryGetProperty("model", out var modelKey) || modelKey.ValueKind != JsonValueKind.String)
            throw new JsonException("rowActionSets requires a Link query and cannot coexist with rowContextMenu.");
        var models = documents.Where(document => document.TryGetProperty(modelKey.GetString()!, out _)).ToArray();
        if (models.Length != 1) throw new JsonException("rowActionSets requires one declared Link model.");
        var model = models[0].GetProperty(modelKey.GetString()!);
        if (!model.TryGetProperty("kind", out var modelKind) || modelKind.ValueKind != JsonValueKind.String || modelKind.GetString() != "Link"
            || !model.TryGetProperty("linkQuery", out var query) || !query.TryGetProperty("endpoints", out var endpoints)
            || endpoints.ValueKind != JsonValueKind.Object) throw new JsonException("rowActionSets requires Link endpoints.");
        var expected = endpoints.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var actions = view.TryGetProperty("rowActions", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().ToArray() : [];
        foreach (var entry in sets.EnumerateObject())
        {
            if (!expected.Contains(entry.Name) || !seen.Add(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object)
                throw new JsonException("Invalid rowActionSets model.");
            var surfaces = entry.Value.EnumerateObject().Select(property => property.Name).ToArray();
            if (surfaces.Length != 2 || !surfaces.Contains("column") || !surfaces.Contains("contextMenu"))
                throw new JsonException("rowActionSets requires column and contextMenu.");
            foreach (var surface in entry.Value.EnumerateObject())
            {
                if (surface.Value.ValueKind != JsonValueKind.Array) throw new JsonException("Action set must be an array.");
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in surface.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) || !keys.Add(item.GetString()!)
                        || actions.Count(action => action.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String && key.GetString() == item.GetString()) != 1)
                        throw new JsonException("Action set reference is missing or duplicate.");
                }
            }
        }
        if (!seen.SetEquals(expected)) throw new JsonException("rowActionSets must cover the declared Link endpoint models.");
    }
}
