using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>从标准导航和 View 引用展开静态位置；复用的 View 在每个位置独立投影。</summary>
public static class StandardFeatureMetadataCompiler
{
    /// <summary>仅投影明确采用位置授权的导航；未切换模块由原授权协议拥有。</summary>
    public static IReadOnlyList<FeatureDeclaration> Read(string moduleId, IEnumerable<JsonElement> documents, IReadOnlyList<FeatureDeclaration>? customFeatures = null)
    {
        var all = documents.ToArray();
        var views = all.Where(document => document.TryGetProperty("view", out _))
            .Select(document => document.GetProperty("view")).ToDictionary(view => Text(view, "key"), StringComparer.Ordinal);
        var navigation = all.Where(document => document.TryGetProperty("navigation", out _))
            .SelectMany(document => document.GetProperty("navigation").GetProperty("items").EnumerateArray())
            .ToDictionary(item => Text(item, "key"), StringComparer.Ordinal);
        var result = new List<FeatureDeclaration>();
        var scopeTitles = new Dictionary<(string Owner, string Scope), string>();
        var entered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in navigation.Values)
            if (item.TryGetProperty("featureAuthorization", out var enabled) && enabled.ValueKind == JsonValueKind.True)
                Navigation(item);
        foreach (var item in navigation.Values)
            if (Optional(item, "featureOwner") is { } owner && !item.TryGetProperty("redirectTo", out _))
                View(Text(item, "targetKey"), owner, null, []);
        return result;

        // 导航祖先也是独立资格，但纯容器不获得任何后代读取行为。
        void Navigation(JsonElement item)
        {
            var key = Text(item, "key");
            if (!entered.Add(key)) return;
            var parent = Optional(item, "parentKey");
            if (parent is not null)
            {
                if (!navigation.TryGetValue(parent, out var parentItem)) throw new InvalidOperationException("导航入口祖先不存在。");
                Navigation(parentItem);
            }
            if (item.TryGetProperty("redirectTo", out _)) throw new InvalidOperationException("重定向别名不能创建授权点。");
            var reference = "navigation:" + key;
            var node = new FeatureDeclaration(moduleId, key, "navigation", Title(Optional(item, "titleKey") ?? key),
                parent is null ? null : "navigation:" + parent, "") { ReferenceKey = reference, Container = true };
            result.Add(node);
            if (item.TryGetProperty("page", out var page)) Page(page, reference, null, []);
            else if (item.TryGetProperty("targetKey", out var target)) View(target.GetString()!, reference, null, []);
        }

        // 页面只传递配置，不创建可以勾选的 page 节点。
        void Page(JsonElement page, string owner, string? scope, HashSet<string> chain)
        {
            if (!page.TryGetProperty("parameters", out var parameters) || !parameters.TryGetProperty("viewKey", out var key))
                throw new InvalidOperationException("标准授权入口缺少 View 引用。");
            View(key.GetString()!, owner, scope, chain);
        }

        // 当前分支检测循环，不把多个合法位置复用同一 View 当成重复。
        void View(string key, string owner, string? scope, HashSet<string> chain)
        {
            if (!views.TryGetValue(key, out var view) || !chain.Add(key)) throw new InvalidOperationException("授权 View 引用缺失或循环。");
            var mode = Text(view, "mode");
            if (mode == "tab")
            {
                foreach (var tab in view.GetProperty("tabs").EnumerateArray())
                {
                    var tabKey = Text(tab, "key");
                    var reference = owner + "/" + (scope is null ? "" : "node-type:" + Uri.EscapeDataString(scope) + "/") + "tab:" + Uri.EscapeDataString(tabKey);
                    result.Add(new(moduleId, tabKey, "tab", Title(Text(tab, "titleKey"), Optional(view, "dicKey")), owner, "")
                    { ReferenceKey = reference, StaticScope = scope, StaticScopeTitle = ScopeTitle(owner, scope), Container = true });
                    Page(tab.GetProperty("pageRef"), reference, null, new(chain, StringComparer.Ordinal));
                }
            }
            else if (mode == "tree")
            {
                BindRead(owner, "tree:" + key);
                TreeActions(view, "treeActions", owner, scope, Optional(view, "dicKey"));
                foreach (var type in view.GetProperty("nodeTypes").EnumerateArray())
                {
                    var nodeType = Text(type, "key");
                    scopeTitles[(owner, nodeType)] = Title(Text(type, "titleKey"), Optional(view, "dicKey"));
                    TreeActions(type, "contextMenu", owner, nodeType, Optional(view, "dicKey"));
                    Page(type.GetProperty("pageRef"), owner, nodeType, new(chain, StringComparer.Ordinal));
                }
            }
            else if (mode is "list" or "detail")
            {
                var resource = Text(view, "resourceKey");
                BindRead(owner, resource + ".read");
                if (mode == "list") foreach (var read in LookupBehaviors(view, false)) BindRead(owner, read);
                Actions(view, "pageActions", owner, scope, resource);
                Actions(view, "rowActions", owner, scope, resource);
                if (view.TryGetProperty("featureActions", out var customActions))
                    foreach (var action in customActions.EnumerateArray())
                        Action(owner, scope, Text(action, "key"), Title(Text(action, "titleKey"), Optional(view, "dicKey")), Text(action, "behaviorKey"), mode + ".custom",
                            action.TryGetProperty("readBehaviors", out var reads) ? reads.EnumerateArray().Select(read => read.GetString()!).ToArray() : []);
                if (mode == "list" && view.TryGetProperty("export", out var export) && (!export.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False))
                    Action(owner, scope, "export", "导出", resource + ".export", "list");
            }
            else throw new InvalidOperationException("不支持的标准授权 View 类型。");
        }

        // 内容入口只获得当前实际承载页面的读取，不递归给予子 Tab 读取。
        void BindRead(string owner, string behavior)
        {
            var index = result.FindIndex(node => node.ReferenceKey == owner);
            var node = result[index];
            result[index] = node with { Container = false, BehaviorKey = node.BehaviorKey.Length == 0 ? behavior : node.BehaviorKey,
                ReadBehaviors = [.. node.ReadBehaviors, behavior] };
        }

        // 查看、排序等纯交互随入口；真实业务动作仍使用稳定动作键。
        void Actions(JsonElement view, string property, string owner, string? scope, string resource)
        {
            if (!view.TryGetProperty(property, out var actions)) return;
            foreach (var action in actions.EnumerateArray())
            {
                // 快捷跳转复用目标导航资格，不形成另一份业务动作授权。
                if (Optional(action, "navigationTargetKey") is { } target)
                {
                    if (!navigation.TryGetValue(target, out var targetEntry)
                        || !(Optional(targetEntry, "featureKey") is { } featureKey
                            ? customFeatures?.Any(feature => feature.Key == featureKey && feature.Kind == "navigation") == true
                            : targetEntry.TryGetProperty("featureAuthorization", out var targetEnabled) && targetEnabled.ValueKind == JsonValueKind.True))
                        throw new InvalidOperationException("动作快捷跳转缺少有效功能导航目标。");
                    continue;
                }
                var operation = Optional(action, "standardAction") ?? Optional(action, "handlerKey");
                if (operation is null or "view" or "refresh") continue;
                Action(owner, scope, Text(action, "key"), Title(Optional(action, "labelKey") ?? Text(action, "key"), Optional(view, "dicKey")),
                    resource + "." + (operation == "edit" ? "update" : operation), Text(view, "mode") + "." + property,
                    (operation is "create" or "edit" ? LookupBehaviors(view, true).Append(resource + ".read") : [resource + ".read"])
                        .Concat(action.TryGetProperty("readBehaviors", out var reads) ? reads.EnumerateArray().Select(read => read.GetString()!) : []).ToArray());
            }
        }

        // 树引用原 View 动作的行为；节点类型范围只参与静态身份。
        void TreeActions(JsonElement view, string property, string owner, string? scope, string? dictionary)
        {
            if (!view.TryGetProperty(property, out var actions)) return;
            foreach (var action in actions.EnumerateArray())
            {
                var target = views[Text(action, "viewKey")];
                var operation = Optional(action, "standardAction") ?? Text(action, "actionKey");
                Action(owner, scope, Text(action, "key"), Title(Text(action, "labelKey"), dictionary),
                    Text(target, "resourceKey") + "." + (operation == "edit" ? "update" : operation), property,
                    operation is "create" or "edit" ? LookupBehaviors(target, true).Append(Text(target, "resourceKey") + ".read").ToArray() : [Text(target, "resourceKey") + ".read"]);
            }
        }

        // Lookup 绑定属于资源 View；同名 provider 在不同资源中不能合并为同一执行行为。
        IReadOnlyList<string> LookupBehaviors(JsonElement view, bool form)
        {
            if (!view.TryGetProperty("lookupBindings", out var bindings)) return [];
            var modelName = Optional(view, "model");
            var model = all.FirstOrDefault(document => modelName is not null && document.TryGetProperty(modelName, out _));
            if (model.ValueKind == JsonValueKind.Undefined) return [];
            var fields = model.GetProperty(modelName!).GetProperty("fields").EnumerateArray().ToDictionary(field => Text(field, "name"), StringComparer.Ordinal);
            var providers = new HashSet<string>(StringComparer.Ordinal);
            if (form)
            {
                foreach (var field in fields.Values) AddField(field);
            }
            else
            {
                if (view.TryGetProperty("query", out var query)) Query(query);
                if (view.TryGetProperty("advancedFilter", out var advanced)) Query(advanced);
            }
            return providers.Select(provider => Optional(bindings, provider)
                ?? throw new InvalidOperationException($"Lookup {provider} 缺少执行绑定。")).Distinct(StringComparer.Ordinal).ToArray();

            // 只读取元数据已经选择的字段或 provider override，不给予整模块读取集合。
            void Query(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Array) { foreach (var item in value.EnumerateArray()) Query(item); return; }
                if (value.ValueKind != JsonValueKind.Object) return;
                if (Optional(value, "dataSourceKey") is { } provider) providers.Add(provider);
                else if (Optional(value, "field") is { } name && fields.TryGetValue(name, out var field)) AddField(field);
                foreach (var property in value.EnumerateObject()) Query(property.Value);
            }

            // Model 声明 provider；字典只绑定稳定行为，不包含 HTTP 地址或权限代码。
            void AddField(JsonElement field)
            {
                if (field.TryGetProperty("dataSource", out var source) && Optional(source, "type") == "provider")
                    providers.Add(Text(source, "key"));
            }
        }
        // 声明引用不替代位置身份；重复的静态位置由统一目录编译器拒绝。
        void Action(string owner, string? scope, string key, string title, string behavior, string surface, IReadOnlyList<string>? reads = null) =>
            result.Add(new(moduleId, key, "action", title, owner, behavior)
            { ReferenceKey = owner + "/" + (scope is null ? "" : "node-type:" + Uri.EscapeDataString(scope) + "/") + "surface:" + Uri.EscapeDataString(surface) + "/action:" + Uri.EscapeDataString(key), StaticScope = scope, StaticScopeTitle = ScopeTitle(owner, scope), ActionScope = surface, ReadBehaviors = reads ?? [] });

        // 展示组复用树类型的字典标题，不向可授权集合增加节点。
        string? ScopeTitle(string owner, string? scope) => scope is not null && scopeTitles.TryGetValue((owner, scope), out var title) ? title : null;

        // 当前目录默认中文名称来自同一 authoring 字典，身份始终使用稳定键。
        string Title(string key, string? dictionary = null)
        {
            var split = key.IndexOf('.');
            if (dictionary is null && split > 0) { dictionary = key[..split]; key = key[(split + 1)..]; }
            if (dictionary is not null)
                foreach (var document in all)
                    if (Optional(document, "type") == "dic" && document.TryGetProperty(dictionary, out var entries)
                        && entries.TryGetProperty(key, out var translations) && Optional(translations, "zhs") is { } text) return text;
            return key;
        }
    }

    // 标准声明缺失稳定引用时拒绝整份候选。
    private static string Text(JsonElement value, string property) => Optional(value, property)
        ?? throw new InvalidOperationException($"标准功能声明缺少 {property}。");

    // 可选项只能是非空字符串，不把非法结构默认为有效身份。
    private static string? Optional(JsonElement value, string property) => value.TryGetProperty(property, out var item)
        && item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()) ? item.GetString() : null;
}
