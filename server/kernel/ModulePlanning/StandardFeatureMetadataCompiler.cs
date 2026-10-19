using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>从标准导航和 View 引用展开静态位置；复用的 View 在每个位置独立投影。</summary>
public static class StandardFeatureMetadataCompiler
{
    /// <summary>仅投影明确采用位置授权的导航；未切换模块由原授权协议拥有。</summary>
    public static IReadOnlyList<FeatureDeclaration> Read(string moduleId, IEnumerable<JsonElement> documents, IReadOnlyList<FeatureDeclaration>? customFeatures = null,
        bool includeCustomFeatures = false)
    {
        var all = documents.ToArray();
        var views = all.Where(document => document.TryGetProperty("view", out _))
            .Select(document => document.GetProperty("view")).ToDictionary(view => Text(view, "key"), StringComparer.Ordinal);
        var navigation = all.Where(document => document.TryGetProperty("navigation", out _))
            .SelectMany(document => document.GetProperty("navigation").GetProperty("items").EnumerateArray())
            .ToDictionary(item => Text(item, "key"), StringComparer.Ordinal);
        var result = customFeatures?.ToList() ?? [];
        var scopeTitles = new Dictionary<(string Owner, string Scope), (string Title, string? TitleKey)>();
        var entered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in navigation.Values)
            if (item.TryGetProperty("featureAuthorization", out var enabled) && enabled.ValueKind == JsonValueKind.True)
                Navigation(item);
        foreach (var item in navigation.Values)
            if (Optional(item, "featureKey") is { } featureKey && customFeatures?.SingleOrDefault(feature => feature.Key == featureKey && feature.Kind == "navigation") is { } custom
                && item.TryGetProperty("page", out var page) && page.TryGetProperty("parameters", out var parameters) && parameters.TryGetProperty("viewKey", out _))
                Page(page, custom.ReferenceKey ?? custom.Key, null, []);
        foreach (var item in navigation.Values)
            if (Optional(item, "featureKey") is { } featureKey && customFeatures?.SingleOrDefault(feature => feature.Key == featureKey && feature.Kind == "navigation") is { } custom
                && item.TryGetProperty("page", out var page) && page.TryGetProperty("dataAccess", out _))
                DataAccess(page, page, "read", "", custom.ReferenceKey ?? custom.Key, Text(page, "pageKey"));
        foreach (var item in navigation.Values)
            if (Optional(item, "featureOwner") is { } owner && !item.TryGetProperty("redirectTo", out _))
                View(Text(item, "targetKey"), owner, null, []);
        var customKeys = customFeatures?.Select(feature => feature.ReferenceKey ?? feature.Key).ToHashSet(StringComparer.Ordinal) ?? [];
        return includeCustomFeatures ? result : result.Where(feature => !customKeys.Contains(feature.ReferenceKey ?? feature.Key)).ToArray();

        // 导航祖先也是独立资格，但纯容器不获得任何后代读取行为。
        void Navigation(JsonElement item)
        {
            var key = Text(item, "key");
            if (!entered.Add(key)) return;
            var parent = Optional(item, "parentKey");
            var parentModule = Optional(item, "parentModuleId");
            var externalParent = parentModule is not null && parentModule != moduleId;
            if (parentModule is not null && (string.IsNullOrWhiteSpace(parentModule) || parent is null))
                throw new InvalidOperationException("跨模块导航必须声明有效父模块与父入口。");
            if (parent is not null && !externalParent)
            {
                if (!navigation.TryGetValue(parent, out var parentItem)) throw new InvalidOperationException("导航入口祖先不存在。");
                Navigation(parentItem);
            }
            if (item.TryGetProperty("redirectTo", out _)) throw new InvalidOperationException("重定向别名不能创建授权点。");
            var reference = "navigation:" + key;
            var node = new FeatureDeclaration(moduleId, key, "navigation", Title(Optional(item, "titleKey") ?? key),
                parent is null ? null : externalParent ? parent : "navigation:" + parent, "") { ParentModuleId = externalParent ? parentModule : null, ReferenceKey = reference, TitleKey = FullTitleKey(Optional(item, "titleKey")), Container = true };
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
                    { ReferenceKey = reference, TitleKey = FullTitleKey(Text(tab, "titleKey"), Optional(view, "dicKey")), StaticScope = scope, StaticScopeTitle = ScopeTitle(owner, scope), StaticScopeTitleKey = ScopeTitleKey(owner, scope), Container = true });
                    Page(tab.GetProperty("pageRef"), reference, null, new(chain, StringComparer.Ordinal));
                }
            }
            else if (mode == "tree")
            {
                BindRead(owner, "tree:" + key);
                DataAccess(view, view, "read", "", owner);
                TreeActions(view, "treeActions", owner, scope, Optional(view, "dicKey"));
                foreach (var type in view.GetProperty("nodeTypes").EnumerateArray())
                {
                    var nodeType = Text(type, "key");
                    scopeTitles[(owner, nodeType)] = (Title(Text(type, "titleKey"), Optional(view, "dicKey")), FullTitleKey(Text(type, "titleKey"), Optional(view, "dicKey")));
                    TreeActions(type, "contextMenu", owner, nodeType, Optional(view, "dicKey"));
                    Page(type.GetProperty("pageRef"), owner, nodeType, new(chain, StringComparer.Ordinal));
                }
            }
            else if (mode is "list" or "detail")
            {
                var resource = Text(view, "resourceKey");
                BindRead(owner, resource + ".read");
                BindDataResource(owner, view, resource);
                DataAccess(view, view, "read", "", owner);
                if (mode == "list") BindDetailRead(view, owner);
                if (view.TryGetProperty("outputActions", out var outputs))
                {
                    if (outputs.EnumerateArray().GroupBy(output => Text(output, "key"), StringComparer.Ordinal).Any(group => group.Count() > 1))
                        throw new InvalidOperationException("输出按钮声明身份冲突。");
                    foreach (var output in outputs.EnumerateArray()) DataAccess(view, output, "outputActions", Text(output, "key"), owner);
                }
                if (view.TryGetProperty("detailContributions", out var contributions))
                {
                    if (contributions.EnumerateArray().GroupBy(contribution => Text(contribution, "key"), StringComparer.Ordinal)
                        .Any(group => group.Count() > 1 && group.Any(contribution => contribution.TryGetProperty("dataAccess", out _))))
                        throw new InvalidOperationException("详情贡献声明身份冲突。");
                    foreach (var contribution in contributions.EnumerateArray())
                    {
                        if (!contribution.TryGetProperty("dataAccess", out _)) continue;
                        var navigationKey = Text(contribution, "navigationKey");
                        if (!navigation.TryGetValue(navigationKey, out var targetEntry))
                            throw new InvalidOperationException("详情贡献的数据声明缺少原功能导航。");
                        var targetOwner = Optional(targetEntry, "featureKey") ?? "navigation:" + navigationKey;
                        DataAccess(view, contribution, "detailContributions", Text(contribution, "key"), targetOwner);
                    }
                }
                if (mode == "list")
                {
                    foreach (var read in LookupBehaviors(view, false)) BindRead(owner, read);
                    BindLookupDataAccess(view, false, owner);
                }
                Actions(view, "pageActions", owner, scope, resource);
                Actions(view, "rowActions", owner, scope, resource);
                if (view.TryGetProperty("featureActions", out var customActions))
                    foreach (var action in customActions.EnumerateArray())
                    {
                        Action(owner, scope, Text(action, "key"), Title(Text(action, "titleKey"), Optional(view, "dicKey")), Text(action, "behaviorKey"), mode + ".custom",
                            action.TryGetProperty("readBehaviors", out var reads) ? reads.EnumerateArray().Select(read => read.GetString()!).ToArray() : [], FullTitleKey(Text(action, "titleKey"), Optional(view, "dicKey")));
                        DataAccess(view, action, "featureActions", Text(action, "key"), result[^1].ReferenceKey!);
                    }
                if (mode == "list" && view.TryGetProperty("export", out var export) && (!export.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False))
                {
                    Action(owner, scope, "export", "导出", resource + ".export", "list", titleKey: "Platform.Export");
                    DataAccess(view, export, "export", "export", result[^1].ReferenceKey!);
                }
            }
            else throw new InvalidOperationException("不支持的标准授权 View 类型。");
        }

        // 内容入口只获得当前实际承载页面的读取，不递归给予子 Tab 读取。
        void BindRead(string owner, string behavior)
        {
            var index = result.FindIndex(node => (node.ReferenceKey ?? node.Key) == owner);
            var node = result[index];
            result[index] = node with { Container = false, BehaviorKey = node.BehaviorKey.Length == 0 ? behavior : node.BehaviorKey,
                ReadBehaviors = [.. node.ReadBehaviors, behavior] };
        }

        // 资源引用只来自真实Entity的既有View绑定，履历等model-less读取行为不伪装成数据资源。
        void BindDataResource(string owner, JsonElement view, string resource)
        {
            var modelKey = Optional(view, "model");
            if (modelKey is null || !all.Any(document => document.TryGetProperty(modelKey, out var model) && Optional(model, "kind") == "Entity")) return;
            var index = result.FindIndex(node => (node.ReferenceKey ?? node.Key) == owner);
            var node = result[index];
            result[index] = node with { DataResources = node.DataResources.Append(resource).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
        }

        // 查看、排序等纯交互随入口；真实业务动作仍使用稳定动作键。
        void Actions(JsonElement view, string property, string owner, string? scope, string resource)
        {
            if (!view.TryGetProperty(property, out var actions)) return;
            if (actions.EnumerateArray().GroupBy(action => Text(action, "key"), StringComparer.Ordinal)
                .Any(group => group.Count() > 1 && group.Any(action => action.TryGetProperty("dataAccess", out _))))
                throw new InvalidOperationException("按钮声明身份冲突。");
            foreach (var action in actions.EnumerateArray())
            {
                // 普通快捷跳转复用目标资格；标准行查看可沿当前读取资格进入无独立授权的呈现页。
                if (Optional(action, "navigationTargetKey") is { } target)
                {
                    if (!navigation.TryGetValue(target, out var targetEntry))
                        throw new InvalidOperationException("动作快捷跳转缺少有效功能导航目标。");
                    var readPresentation = Text(view, "mode") == "list" && property == "rowActions"
                        && Optional(action, "standardAction") == "view" && Optional(action, "handlerKey") is null
                        && Optional(action, "inputKey") is null && Optional(targetEntry, "featureKey") is null
                        && Optional(targetEntry, "featureOwner") is null && !targetEntry.TryGetProperty("redirectTo", out _)
                        && targetEntry.TryGetProperty("routeOnly", out var routeOnly) && routeOnly.ValueKind == JsonValueKind.True
                        && targetEntry.TryGetProperty("featureAuthorization", out var independent) && independent.ValueKind == JsonValueKind.False
                        && targetEntry.TryGetProperty("page", out _);
                    var authorizedNavigation = Optional(targetEntry, "featureKey") is { } featureKey
                        ? customFeatures?.Any(feature => feature.Key == featureKey && feature.Kind == "navigation") == true
                        : targetEntry.TryGetProperty("featureAuthorization", out var targetEnabled) && targetEnabled.ValueKind == JsonValueKind.True;
                    if (!readPresentation && !authorizedNavigation)
                        throw new InvalidOperationException("动作快捷跳转缺少有效功能导航目标。");
                    var targetOwner = Optional(targetEntry, "featureKey") ?? "navigation:" + target;
                    DataAccess(view, action, property, Text(action, "key"), targetOwner);
                    continue;
                }
                var operation = Optional(action, "standardAction") ?? Optional(action, "handlerKey");
                if (operation is null or "view" or "refresh")
                {
                    DataAccess(view, action, property, Text(action, "key"), owner);
                    continue;
                }
                Action(owner, scope, Text(action, "key"), Title(Optional(action, "labelKey") ?? Text(action, "key"), Optional(view, "dicKey")),
                    resource + "." + (operation == "edit" ? "update" : operation), Text(view, "mode") + "." + property,
                    ActionReads(view, action, operation),
                    FullTitleKey(Optional(action, "labelKey") ?? Text(action, "key"), Optional(view, "dicKey")));
                DataAccess(view, action, property, Text(action, "key"), result[^1].ReferenceKey!);
                if (operation is "create" or "edit") BindLookupDataAccess(view, true, result[^1].ReferenceKey!);
                if (operation == "edit") BindDetailRead(view, result[^1].ReferenceKey!);
            }
        }

        // 编辑预取使用真实详情读取声明；位置仍是原编辑资格，不借列表导航或新造入口。
        void BindDetailRead(JsonElement view, string owner)
        {
            if (Optional(view, "mode") == "detail") DataAccess(view, view, "read", "", owner);
            else if (Optional(view, "detailViewKey") is { } key && views.TryGetValue(key, out var detail))
                DataAccess(detail, detail, "read", "", owner);
        }

        // 直接要求附在已有功能位置；查看和输出不因此增加可授权动作。
        void DataAccess(JsonElement view, JsonElement action, string surface, string key, string owner, string? sourceKey = null)
        {
            if (!action.TryGetProperty("dataAccess", out var declared)) return;
            var index = result.FindIndex(feature => (feature.ReferenceKey ?? feature.Key) == owner);
            if (index < 0) throw new InvalidOperationException("数据声明缺少合法功能归属。");
            var node = result[index];
            if (declared.EnumerateObject().Any(property => property.Name is not ("execution" or "resources" or "actions" or "additionalActions")))
                throw new InvalidOperationException("ACTION_DATA_ACCESS_DECLARATION_INVALID");
            var declaration = new ActionDataAccessDeclaration(moduleId, sourceKey ?? Text(view, "key"), surface, key, Text(declared, "execution"),
                declared.GetProperty("resources").EnumerateArray().Select(item => item.GetString()!).ToArray(),
                declared.TryGetProperty("actions", out var declaredActions) ? declaredActions.EnumerateArray().Select(item => item.GetString()!).ToArray() : [])
            { AdditionalActions = declared.TryGetProperty("additionalActions", out var additional) ? additional.EnumerateArray().Select(item => item.GetString()!).ToArray() : [] };
            // 相同 View 可由多个树节点引用到同一功能；相同结构的展开不制造重复声明。
            if (!node.DataAccess.Any(item => item.Id == declaration.Id)) result[index] = node with { DataAccess = [.. node.DataAccess, declaration] };
        }

        // 树引用原 View 动作的行为；节点类型范围只参与静态身份。
        void TreeActions(JsonElement view, string property, string owner, string? scope, string? dictionary)
        {
            if (!view.TryGetProperty(property, out var actions)) return;
            foreach (var action in actions.EnumerateArray())
            {
                var target = views[Text(action, "viewKey")];
                var standard = Optional(action, "standardAction");
                var key = standard ?? Text(action, "actionKey");
                var referenced = new[] { "pageActions", "rowActions", "featureActions" }.SelectMany(name => target.TryGetProperty(name, out var entries)
                    ? entries.EnumerateArray().Select(item => (Surface: name, Definition: item)).ToArray() : [])
                    .Single(item => Optional(item.Definition, standard is null ? "key" : "standardAction") == key
                        && (Optional(item.Definition, "handlerKey") is not null || Optional(item.Definition, "standardAction") is not null));
                var definition = referenced.Definition;
                var operation = Optional(definition, "standardAction") ?? Optional(definition, "handlerKey")
                    ?? throw new InvalidOperationException("树动作引用缺少资源执行行为。");
                var behavior = Text(target, "resourceKey") + "." + (operation is "view" or "refresh" ? "read" : operation == "edit" ? "update" : operation);
                if (Optional(definition, "behaviorKey") is { } declared && declared != behavior)
                    throw new InvalidOperationException("树动作的功能声明与资源执行行为不一致。");
                Action(owner, scope, Text(action, "key"), Title(Text(action, "labelKey"), dictionary),
                    Text(target, "resourceKey") + "." + (operation is "view" or "refresh" ? "read" : operation == "edit" ? "update" : operation), property,
                    ActionReads(target, definition, operation),
                    FullTitleKey(Text(action, "labelKey"), dictionary));
                DataAccess(target, definition, referenced.Surface, Text(definition, "key"), result[^1].ReferenceKey!);
                if (operation is "create" or "edit") BindLookupDataAccess(target, true, result[^1].ReferenceKey!);
                if (operation == "edit") BindDetailRead(target, result[^1].ReferenceKey!);
            }
        }

        // 所有呈现位置复用资源动作自己的表单 Lookup 与显式附加读取，不从引用 key 猜测执行行为。
        string[] ActionReads(JsonElement view, JsonElement action, string operation) =>
            (operation is "create" or "edit" ? LookupBehaviors(view, true).Append(Text(view, "resourceKey") + ".read") : [Text(view, "resourceKey") + ".read"])
                .Concat(action.TryGetProperty("readBehaviors", out var reads) ? reads.EnumerateArray().Select(read => read.GetString()!) : []).ToArray();

        // Lookup 绑定属于资源 View；同名 provider 在不同资源中不能合并为同一执行行为。
        IReadOnlyList<string> LookupBehaviors(JsonElement view, bool form) =>
            LookupSources(view, form).Select(source => source.Behavior).Distinct(StringComparer.Ordinal).ToArray();

        // 声明附在同一既有读取/表单位置；provider key只选择真实视图已使用的来源。
        void BindLookupDataAccess(JsonElement view, bool form, string owner)
        {
            foreach (var source in LookupSources(view, form))
                if (source.Definition.ValueKind == JsonValueKind.Object) DataAccess(view, source.Definition, "lookupBindings", source.Key, owner);
        }

        // 旧字符串保留所属专用接口；采用者在相同来源位置增加正式执行引用。
        IReadOnlyList<(string Key, string Behavior, JsonElement Definition)> LookupSources(JsonElement view, bool form)
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
            return providers.Select(provider =>
            {
                if (!bindings.TryGetProperty(provider, out var definition)) throw new InvalidOperationException($"Lookup {provider} 缺少执行绑定。");
                if (definition.ValueKind == JsonValueKind.Object && definition.EnumerateObject().Any(property => property.Name is not ("behavior" or "dataAccess")))
                    throw new InvalidOperationException("LOOKUP_DATA_ACCESS_BINDING_INVALID");
                var behavior = definition.ValueKind == JsonValueKind.String ? definition.GetString()! : Text(definition, "behavior");
                return (provider, behavior, definition);
            }).ToArray();

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
        void Action(string owner, string? scope, string key, string title, string behavior, string surface, IReadOnlyList<string>? reads = null, string? titleKey = null) =>
            result.Add(new(moduleId, key, "action", title, owner, behavior)
            { ReferenceKey = owner + "/" + (scope is null ? "" : "node-type:" + Uri.EscapeDataString(scope) + "/") + "surface:" + Uri.EscapeDataString(surface) + "/action:" + Uri.EscapeDataString(key), TitleKey = titleKey, StaticScope = scope, StaticScopeTitle = ScopeTitle(owner, scope), StaticScopeTitleKey = ScopeTitleKey(owner, scope), ActionScope = surface, ReadBehaviors = reads ?? [] });

        // 展示组复用树类型的字典标题，不向可授权集合增加节点。
        string? ScopeTitle(string owner, string? scope) => scope is not null && scopeTitles.TryGetValue((owner, scope), out var title) ? title.Title : null;

        // 作用域译文键与原展示组标题来自同一个节点类型声明。
        string? ScopeTitleKey(string owner, string? scope) => scope is not null && scopeTitles.TryGetValue((owner, scope), out var title) ? title.TitleKey : null;

        // 只保留确切字典引用；无字典的旧字面标题继续使用原 Title。
        string? FullTitleKey(string? key, string? dictionary = null) => key is null ? null
            : dictionary is not null ? dictionary + "." + key : key.Contains('.') ? key : null;

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
