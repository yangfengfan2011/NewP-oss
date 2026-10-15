using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>正式候选读取中的 PageRef、Tree／Tab、详情贡献及当前引用链校验。</summary>
public static class PageCompositionMetadataValidator
{
    private static readonly IReadOnlyDictionary<string, string> StandardModes = new Dictionary<string, string>
    {
        ["platform.tree"] = "tree", ["platform.tabs"] = "tab", ["platform.list"] = "list", ["platform.detail"] = "detail"
    };

    /// <summary>校验同一模块的组合配置并返回其受控行为引用；字典与 View 不生成页面身份。</summary>
    public static IReadOnlyList<string> Validate(IEnumerable<JsonElement> documents, IReadOnlySet<string>? bindings)
    {
        var all = documents.ToArray();
        var views = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var document in all)
        {
            if (!document.TryGetProperty("view", out var view)) continue;
            var key = Required(view, "key");
            if (!views.TryAdd(key, view)) throw new JsonException($"Duplicate View: {key}");
        }
        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var view in views.Values.Where(view => Mode(view) == "list"))
        {
            ValidateColumnResizing(view);
            RowActionSetMetadataValidator.Validate(view, all);
            if (!view.TryGetProperty("rowContextMenu", out var menu)) continue;
            if (menu.ValueKind != JsonValueKind.Array) throw new JsonException("Row context menu must be an array of action keys.");
            var actions = view.TryGetProperty("rowActions", out var rows) && rows.ValueKind == JsonValueKind.Array
                ? rows.EnumerateArray().ToArray() : [];
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in menu.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())
                    || !keys.Add(item.GetString()!) || actions.Count(action => Required(action, "key") == item.GetString()) != 1)
                    throw new JsonException("Row context menu reference is invalid, missing or duplicate.");
            }
        }
        var contributions = new HashSet<string>(StringComparer.Ordinal);
        var contributionTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var view in views.Values)
        {
            if (!view.TryGetProperty("detailContributions", out var entries)) continue;
            if (Mode(view) != "list" || entries.ValueKind != JsonValueKind.Array) throw new JsonException("Detail contributions require a List View array.");
            Title(Required(view, "dicKey"), view);
            foreach (var entry in entries.EnumerateArray())
            {
                if (!contributions.Add(Required(entry, "key"))) throw new JsonException("Duplicate detail contribution key.");
                if (!contributionTargets.Add(Required(view, "key") + ":" + Required(entry, "targetViewKey"))) throw new JsonException("Duplicate detail contribution target.");
                var navigationKey = Required(entry, "navigationKey");
                if (all.Count(document => document.TryGetProperty("navigation", out var navigation) && navigation.TryGetProperty("items", out var items)
                    && items.EnumerateArray().Any(item => Required(item, "key") == navigationKey)) != 1) throw new JsonException("Detail contribution requires an owning navigation.");
                var page = entry.GetProperty("pageRef");
                if (Required(page, "pageKey") != "platform.list" || Required(page.GetProperty("parameters"), "viewKey") != Required(view, "key"))
                    throw new JsonException("Detail contribution must use its owning List View.");
                _ = Required(page.GetProperty("parameters"), "anchorModelKey");
                if (page.GetProperty("parameters").TryGetProperty("recordId", out _)) throw new JsonException("Detail contribution recordId belongs to the host.");
                CheckPage(page, []);
                Bind(Required(view, "key"));
            }
        }
        foreach (var view in views.Values.Where(view => Mode(view) is "tree" or "tab"))
            CheckView(view, new HashSet<string>(StringComparer.Ordinal));
        foreach (var document in all)
            if (document.TryGetProperty("navigation", out var navigation) && navigation.TryGetProperty("items", out var items))
                foreach (var item in items.EnumerateArray()) if (item.TryGetProperty("page", out var page)) CheckPage(page, []);
        return references.ToArray();

        // 页面固定参数只引用已声明 View；缺失目标不能回退为其他页面。
        void CheckPage(JsonElement page, HashSet<string> chain)
        {
            var pageKey = Required(page, "pageKey");
            if (page.TryGetProperty("inputResolverKey", out _)) Bind(Required(page, "inputResolverKey"));
            if (!StandardModes.TryGetValue(pageKey, out var expected)) { Bind(pageKey); return; }
            if (!page.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
                throw new JsonException("Standard page requires parameters.");
            var key = Required(parameters, "viewKey");
            if (!views.TryGetValue(key, out var target) || Mode(target) != expected)
                throw new JsonException($"Page View mismatch: {pageKey}/{key}");
            if (expected is "tree" or "tab") CheckView(target, chain);
        }
        // 每个递归分支使用当前路径，不把合法的兄弟复用或相同页面不同配置误认为环。
        void CheckView(JsonElement view, HashSet<string> chain)
        {
            var key = Required(view, "key");
            if (!chain.Add(key)) throw new JsonException($"Cyclic page configuration: {key}");
            var dictionary = Required(view, "dicKey");
            Title(dictionary, view);
            if (Mode(view) == "tab")
            {
                var tabs = view.GetProperty("tabs").EnumerateArray().ToArray();
                var keys = tabs.Select(tab => Required(tab, "key")).ToArray();
                if (keys.Length == 0 || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length
                    || !keys.Contains(Required(view, "defaultTabKey"), StringComparer.Ordinal)) throw new JsonException("Invalid default or duplicate tab.");
                foreach (var tab in tabs) { Title(dictionary, tab); CheckPage(tab.GetProperty("pageRef"), new(chain, StringComparer.Ordinal)); }
            }
            else
            {
                if (view.TryGetProperty("searchEnabled", out var searchEnabled) && searchEnabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new JsonException("Tree searchEnabled must be boolean.");
                var providerKey = Required(view, "providerKey");
                if (view.TryGetProperty("linkSource", out _)) LinkTreeMetadata.Read(view);
                else if (providerKey == RelationTreeMetadata.ProviderKey) RelationTreeMetadata.Read(view);
                if (providerKey != RelationTreeMetadata.ProviderKey)
                {
                    if (view.TryGetProperty("relationSource", out _)) throw new JsonException("Custom Provider cannot declare relationSource.");
                    Bind(providerKey);
                }
                CheckActions(view, "treeActions", dictionary);
                var types = view.GetProperty("nodeTypes").EnumerateArray().ToArray();
                if (types.Length == 0 || types.Select(type => Required(type, "key")).Distinct(StringComparer.Ordinal).Count() != types.Length)
                    throw new JsonException("Invalid tree nodeTypes.");
                foreach (var type in types)
                {
                    Title(dictionary, type); CheckPage(type.GetProperty("pageRef"), new(chain, StringComparer.Ordinal));
                    CheckActions(type, "contextMenu", dictionary);
                }
            }
        }
        // 树只引用资源已经允许的动作；行为实现由该 List View 的受控注册拥有。
        void CheckActions(JsonElement owner, string property, string dictionary)
        {
            if (!owner.TryGetProperty(property, out var actions)) return;
            if (actions.ValueKind != JsonValueKind.Array) throw new JsonException("Tree actions must be an array.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in actions.EnumerateArray())
            {
                if (!keys.Add(Required(reference, "key"))) throw new JsonException("Duplicate tree action key.");
                Title(dictionary, reference, "labelKey");
                if (reference.TryGetProperty("inputResolverKey", out _)) Bind(Required(reference, "inputResolverKey"));
                var listKey = Required(reference, "viewKey");
                if (!views.TryGetValue(listKey, out var list) || Mode(list) != "list") throw new JsonException("Tree action requires a List View.");
                Bind(listKey);
                var standard = reference.TryGetProperty("standardAction", out _);
                if (standard == reference.TryGetProperty("actionKey", out _)) throw new JsonException("Tree action requires exactly one action reference.");
                var target = Required(reference, standard ? "standardAction" : "actionKey");
                var matches = new[] { "pageActions", "rowActions", "featureActions" }.SelectMany(name => list.TryGetProperty(name, out var items) ? items.EnumerateArray().ToArray() : [])
                    .Where(action => (action.TryGetProperty("handlerKey", out _) || action.TryGetProperty("standardAction", out _)) && action.TryGetProperty(standard ? "standardAction" : "key", out var value) && value.GetString() == target).ToArray();
                if (matches.Length != 1) throw new JsonException("Tree action reference is missing or ambiguous.");
            }
        }
        // 组合标签和节点类型名称必须引用当前 authoring 词典。
        void Title(string dictionary, JsonElement item, string property = "titleKey")
        {
            var title = Required(item, property);
            if (!all.Any(document => document.TryGetProperty(dictionary, out var words) && words.ValueKind == JsonValueKind.Object && words.TryGetProperty(title, out _)))
                throw new JsonException($"Missing composition dictionary title: {dictionary}.{title}");
        }
        // bindings 由胶囊声明拥有，执行实现仍由前端候选注册核对。
        void Bind(string key)
        {
            if (bindings is not null && !bindings.Contains(key)) throw new JsonException($"Missing composition binding: {key}");
            references.Add(key);
        }
    }

    // 兼容既有未显式声明 mode 的 List View；组合自身必须显式声明 mode。
    private static string Mode(JsonElement view) => view.TryGetProperty("mode", out var mode) ? mode.GetString() ?? "list" : "list";

    // 缺失或空白静态引用在候选边界给出直接诊断。
    private static string Required(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new JsonException($"Missing composition property: {name}");
    /// <summary>列宽拖动开关与单列覆盖必须是布尔值，声明宽度必须为有限正数。</summary>
    private static void ValidateColumnResizing(JsonElement view)
    {
        if (view.TryGetProperty("columnResize", out var enabled) && enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException("columnResize must be boolean.");
        if (!view.TryGetProperty("columns", out var columns) || columns.ValueKind != JsonValueKind.Array) return;
        foreach (var column in columns.EnumerateArray())
        {
            if (column.TryGetProperty("resizable", out var resizable) && resizable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new JsonException("column resizable must be boolean.");
            if (column.TryGetProperty("width", out var width)
                && (width.ValueKind != JsonValueKind.Number || !width.TryGetDouble(out var value) || !double.IsFinite(value) || value <= 0))
                throw new JsonException("column width must be positive.");
        }
    }

}
