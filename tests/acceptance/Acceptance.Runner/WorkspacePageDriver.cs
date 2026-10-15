using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace ICP.Platform.Acceptance;

/// <summary>工作空间唯一正式旅程的页面控件知识；后续内容操作继续扩展此驱动。</summary>
internal sealed class WorkspacePageDriver(IPage page)
{
    /// <summary>通过 Shell 的真实语言控件选择语言；只供原 Native 详情呈现核对使用。</summary>
    internal async Task SelectLanguageAsync(string language)
    {
        await page.Locator(".np-shell-language-select .ant-select-selector").ClickAsync();
        await page.Locator(".ant-select-dropdown:visible").GetByText(language, new() { Exact = true }).ClickAsync();
    }
    internal async Task OpenAsync()
    {
        await page.Locator(".np-sidebar-menu").GetByRole(AriaRole.Link, new() { Name = "工作空间", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/workspace");
        var tree = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
        await tree.GetByText("公共空间", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await tree.GetByText("个人空间", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    internal async Task SelectRootAsync(string label)
    {
        var tree = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
        var content = page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true });
        // 共享树首次会自动选择公共根，不能等待一次并不存在的重复选中请求。
        if (!await content.GetByRole(AriaRole.Heading, new() { Name = label, Exact = true }).IsVisibleAsync())
        {
            var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET"
                && new Uri(response.Url).AbsolutePath == "/api/links/workspace.content");
            await tree.GetByText(label, new() { Exact = true }).ClickAsync();
            var response = await responseTask;
            if (!response.Ok) throw new InvalidOperationException($"文件夹内容请求失败：{response.Status} {await response.TextAsync()}");
        }
        await WaitForContentAsync(label);
    }

    private async Task WaitForContentAsync(string label)
    {
        var content = page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true });
        await content.GetByRole(AriaRole.Heading, new() { Name = label, Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await content.Locator(".ant-table").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        // 共享列表有独立容器/表格加载者，全部可见指示器退出后才交给后续交互。
        await Assertions.Expect(content.Locator(".ant-spin-spinning:visible")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
    }

    internal async Task<string> SelectTreePathAsync(params string[] labels)
    {
        await new StandardTreePageDriver(page).SelectPathAsync(labels);
        await WaitForContentAsync(labels[^1]);
        return page.Url;
    }

    internal async Task RestorePositionAsync(string address, params string[] labels)
    {
        await page.GotoAsync(address);
        await WaitForContentAsync(labels[^1]);
        await new StandardTreePageDriver(page).WaitForBreadcrumbAsync(labels);
    }

    internal async Task RestoreMissingPositionAsync(string address)
    {
        await page.GotoAsync(address);
        await page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true })
            .GetByRole(AriaRole.Status).GetByText("目标节点已不存在", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await WaitForContentAsync("公共空间");
        await new StandardTreePageDriver(page).WaitForBreadcrumbAsync("公共空间");
        await page.WaitForURLAsync(url => url != address);
    }

    internal async Task CreateFolderAsync(string name)
    {
        var crud = new StandardCrudPageDriver(page);
        var drawer = await crud.OpenCreateDrawerAsync("新建文件夹", "新建");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "名称", name);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "说明", "工作空间正式创建旅程");
        await crud.SaveAndCloseAsync(drawer, new("POST", "/api/workspace/content/folders", null, "创建子文件夹"));
        await crud.WaitForRowAsync(name);
    }

    /// <summary>从混合内容行打开编辑 Drawer；业务值与权限由 API 集成测试验证。</summary>
    internal async Task EditObjectAsync(string name, string newName)
    {
        var crud = new StandardCrudPageDriver(page);
        var row = await crud.WaitForRowAsync(name);
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "编辑", Exact = true }).ClickAsync();
        await SaveObjectEditAsync(newName);
        await crud.WaitForRowAsync(newName);
    }

    /// <summary>通过正式列表鼠标菜单编辑所指行，保留与原行入口相同的保存交接。</summary>
    internal async Task EditObjectFromContextMenuAsync(string name, string newName, Func<Task>? capture = null)
    {
        var crud = new StandardCrudPageDriver(page);
        var row = await crud.WaitForRowAsync(name);
        await row.ClickAsync(new() { Button = MouseButton.Right });
        var edit = page.GetByRole(AriaRole.Menuitem, new() { Name = "编辑", Exact = true });
        await edit.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await CaptureContextMenuAsync(capture);
        await edit.ClickAsync();
        await SaveObjectEditAsync(newName);
        await crud.WaitForRowAsync(newName);
    }

    /// <summary>从正式树节点菜单编辑文件夹，保存后仍选中同一出现位置。</summary>
    internal async Task EditFolderFromTreeContextMenuAsync(string name, string newName, Func<Task>? capture = null)
    {
        var tree = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
        await tree.GetByText(name, new() { Exact = true }).ClickAsync(new() { Button = MouseButton.Right });
        var edit = page.GetByRole(AriaRole.Menuitem, new() { Name = "编辑", Exact = true });
        await edit.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await CaptureContextMenuAsync(capture);
        await edit.ClickAsync();
        await SaveObjectEditAsync(newName);
        await WaitForContentAsync(newName);
    }

    /// <summary>沿树菜单提交单条关系换位，等待正式树和列表读取消费已提交变化。</summary>
    internal async Task ReorderFolderAsync(string name, string direction, Func<Task>? capture = null)
    {
        var label = direction switch { "up" => "上移", "down" => "下移", _ => throw new ArgumentException("Unsupported tree direction.", nameof(direction)) };
        var tree = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
        await tree.GetByText(name, new() { Exact = true }).ClickAsync(new() { Button = MouseButton.Right });
        await WaitForContentAsync(name);
        var action = page.GetByRole(AriaRole.Menuitem, new() { Name = label, Exact = true });
        await action.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await CaptureContextMenuAsync(capture);
        var written = false;
        var treeRead = page.WaitForResponseAsync(response => written && response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath == "/api/workspace/content/children");
        var listRead = page.WaitForResponseAsync(response => written && response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath == "/api/links/workspace.content");
        var write = page.WaitForResponseAsync(response =>
        {
            if (response.Request.Method != "POST" || new Uri(response.Url).AbsolutePath != "/api/workspace/content/tree-order") return false;
            written = true;
            return true;
        });
        await action.ClickAsync();
        var response = await write;
        if (!response.Ok) throw new InvalidOperationException($"文件夹换位失败：{response.Status} {await response.TextAsync()}");
        foreach (var read in await Task.WhenAll(treeRead, listRead))
            if (!read.Ok) throw new InvalidOperationException($"文件夹换位后读取失败：{read.Status} {await read.TextAsync()}");
        await WaitForContentAsync(name);
    }

    /// <summary>从当前行的正式右键菜单复制引用；不读取或断言业务对象身份。</summary>
    internal async Task CopyReferenceFromContextMenuAsync(string name, Func<Task>? capture = null)
    {
        var row = await new StandardCrudPageDriver(page).WaitForRowAsync(name);
        await row.ClickAsync(new() { Button = MouseButton.Right });
        var action = page.GetByRole(AriaRole.Menuitem, new() { Name = "复制", Exact = true });
        await action.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await Assertions.Expect(action).ToBeEnabledAsync();
        await CaptureContextMenuAsync(capture);
        await CopyReferencesAsync(() => action.ClickAsync());
    }

    /// <summary>树菜单复制所指文件夹引用，保持与右侧列表相同的实例剪贴板。</summary>
    internal async Task CopyFolderFromTreeContextMenuAsync(string name, Func<Task>? capture = null)
    {
        var tree = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
        await tree.GetByText(name, new() { Exact = true }).ClickAsync(new() { Button = MouseButton.Right });
        await WaitForContentAsync(name);
        var action = page.GetByRole(AriaRole.Menuitem, new() { Name = "复制", Exact = true });
        await action.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await Assertions.Expect(action).ToBeEnabledAsync();
        await CaptureContextMenuAsync(capture);
        await CopyReferencesAsync(() => action.ClickAsync());
    }

    /// <summary>从当前页勾选集合执行正式工具栏复制。</summary>
    internal async Task CopySelectedAsync(params string[] names)
    {
        var crud = new StandardCrudPageDriver(page);
        foreach (var name in names) await (await crud.WaitForRowAsync(name)).GetByRole(AriaRole.Checkbox).CheckAsync();
        await CopyReferencesAsync(() => page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true })
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^复\s*制$") }).ClickAsync());
    }

    private async Task CopyReferencesAsync(Func<Task> trigger)
    {
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath == "/api/workspace/content/clipboard/validate");
        await trigger();
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"复制引用预检失败：{response.Status} {await response.TextAsync()}");
        await response.FinishedAsync();
        await Assertions.Expect(page.Locator(".ant-drawer-content:visible")).ToHaveCountAsync(0);
    }

    /// <summary>在当前目录沿工具栏粘贴，等一次提交及正式列表读取后看到结果。</summary>
    internal async Task PasteReferencesAsync(params string[] names)
        => await PasteReferencesAsync("粘贴引用", names);

    /// <summary>剪切使用同一粘贴动作的待移动文案，成功后仍沿正式读取交接。</summary>
    internal async Task PasteCutReferencesAsync(params string[] names)
        => await PasteReferencesAsync("粘贴引用（待移动）", names);

    private async Task PasteReferencesAsync(string actionLabel, IReadOnlyList<string> names)
    {
        var crud = new StandardCrudPageDriver(page);
        var written = false;
        var read = page.WaitForResponseAsync(response => written && response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath == "/api/links/workspace.content");
        var write = page.WaitForResponseAsync(response =>
        {
            if (response.Request.Method != "POST" || new Uri(response.Url).AbsolutePath != "/api/workspace/content/paste") return false;
            written = true; return true;
        });
        await crud.ClickPageActionAsync(actionLabel);
        var response = await write;
        if (!response.Ok) throw new InvalidOperationException($"粘贴引用失败：{response.Status} {await response.TextAsync()}");
        var refreshed = await read;
        if (!refreshed.Ok) throw new InvalidOperationException($"粘贴后的读取失败：{refreshed.Status} {await refreshed.TextAsync()}");
        foreach (var name in names) await crud.WaitForRowAsync(name);
    }

    /// <summary>从实际树节点标记剪切，预检完成前不进入粘贴步骤。</summary>
    internal async Task CutFolderFromTreeContextMenuAsync(string name, Func<Task>? capture = null)
    {
        var tree = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
        await tree.GetByText(name, new() { Exact = true }).ClickAsync(new() { Button = MouseButton.Right });
        await WaitForContentAsync(name);
        var action = page.GetByRole(AriaRole.Menuitem, new() { Name = "剪切", Exact = true });
        await Assertions.Expect(action).ToBeEnabledAsync();
        await CaptureContextMenuAsync(capture);
        await CutReferencesAsync(() => action.ClickAsync());
    }

    /// <summary>只取当前内容页勾选集合，由正式工具栏执行一次剪切预检。</summary>
    internal async Task CutSelectedAsync(params string[] names)
    {
        var crud = new StandardCrudPageDriver(page);
        foreach (var name in names) await (await crud.WaitForRowAsync(name)).GetByRole(AriaRole.Checkbox).CheckAsync();
        await CutReferencesAsync(() => page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true })
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^剪\s*切$") }).ClickAsync());
    }

    private async Task CutReferencesAsync(Func<Task> trigger)
    {
        var pending = page.WaitForResponseAsync(response => response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath == "/api/workspace/content/clipboard/cut/validate");
        await trigger();
        var response = await pending;
        if (!response.Ok) throw new InvalidOperationException($"剪切预检失败：{response.Status} {await response.TextAsync()}");
        await response.FinishedAsync();
        await page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true })
            .GetByRole(AriaRole.Button, new() { Name = "粘贴引用（待移动）", Exact = true })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>经明确保留对象与文件的确认后移除所选引用，等待原行离开列表。</summary>
    internal async Task RemoveSelectedReferencesAsync(string[] names, Func<ILocator, Task>? capture = null)
    {
        var crud = new StandardCrudPageDriver(page);
        foreach (var name in names) await (await crud.WaitForRowAsync(name)).GetByRole(AriaRole.Checkbox).CheckAsync();
        await crud.ClickPageActionAsync("批量移除引用");
        var dialog = crud.ConfirmDialogWithText("仅从当前文件夹移除所选引用，保留对象及文件，确定继续？");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        if (capture is not null) await capture(dialog);
        await crud.ConfirmAsync(dialog, new("POST", "/api/workspace/content/references/remove", 200, "批量移除引用"));
        foreach (var name in names) await crud.TableRow(name).WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    // 可见布局早于 Ant 入场动画完成；呈现采样等待真实动画，而不是用固定延时猜时机。
    private async Task CaptureContextMenuAsync(Func<Task>? capture)
    {
        if (capture is null) return;
        var popup = page.Locator(".ant-dropdown:visible");
        await Assertions.Expect(popup).ToHaveCSSAsync("opacity", "1");
        await popup.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
        await capture();
    }

    // 列表和树复用真实输入与保存按钮；不复制另一套 API 写入或确认流程。
    private async Task SaveObjectEditAsync(string newName)
    {
        var crud = new StandardCrudPageDriver(page);
        var drawer = await crud.WaitForVisibleDrawerAsync("编辑对象");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "名称", newName);
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "PUT"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/content/", StringComparison.Ordinal));
        await crud.SaveAndCloseWithoutResponseAsync(drawer);
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"编辑对象失败：{response.Status} {await response.TextAsync()}");
    }

    /// <summary>勾选当前页多个真实内容行，确认后等待一次批量写入及列表刷新。</summary>
    internal async Task DeleteSelectedAsync(params string[] names)
    {
        var crud = new StandardCrudPageDriver(page);
        foreach (var name in names)
        {
            var row = await crud.WaitForRowAsync(name);
            await row.GetByRole(AriaRole.Checkbox).CheckAsync();
        }
        await crud.ClickPageActionAsync("批量删除");
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "批量删除", Exact = true });
        await crud.ConfirmAsync(dialog, new("POST", "/api/workspace/content/delete", 200, "批量删除对象"));
        foreach (var name in names)
            await page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true }).Locator("tr.ant-table-row")
                .Filter(new() { HasTextString = name }).WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    internal async Task ExpandRootToFolderAsync(string rootLabel, string folderName)
    {
        var tree = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
        var root = tree.Locator(".ant-tree-treenode[aria-expanded]").Filter(new() { HasTextString = rootLabel }).First;
        await root.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        if (await root.GetAttributeAsync("aria-expanded") != "true") await root.Locator(".ant-tree-switcher").ClickAsync();
        await tree.GetByText(folderName, new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>文件夹属性从内容行的名称链接打开，继续使用真实实体读取。</summary>
    internal async Task<ILocator> OpenFolderPropertiesAsync(string name)
    {
        var crud = new StandardCrudPageDriver(page);
        var row = await crud.WaitForRowAsync(name);
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/folders/", StringComparison.Ordinal)
            && new Uri(response.Url).AbsolutePath != "/api/workspace/folders/candidates");
        await row.GetByRole(AriaRole.Button, new() { Name = name, Exact = true }).ClickAsync();
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"文件夹属性请求失败：{response.Status} {await response.TextAsync()}");
        return await WaitForFolderDrawerAsync(name);
    }

    // 嵌套反向 Modal 可以同时保留两个 Drawer，按实际对象标签定位，不能猜最后一个门户。
    internal async Task<ILocator> WaitForFolderDrawerAsync(string name)
    {
        var drawer = page.Locator(".ant-drawer-content:visible").Filter(new() { Has = page.GetByText(name, new() { Exact = true }) });
        await drawer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await drawer.GetByText(name, new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await drawer.GetByText("基本信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await drawer.GetByText("审计信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await drawer.Locator(".ant-drawer-close").ClickAsync(new() { Trial = true });
        return drawer;
    }

    internal Task ClosePropertiesAsync(ILocator drawer) => new StandardCrudPageDriver(page).CloseDrawerAsync(drawer);

    internal async Task<ILocator> OpenDirectReferencesAsync(ILocator detail, string parentName)
    {
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath == "/api/links/workspace.content"
            && System.Web.HttpUtility.ParseQueryString(new Uri(response.Url).Query)["direction"] == "reverse");
        await detail.GetByRole(AriaRole.Button, new() { Name = "直接引用文件夹", Exact = true }).ClickAsync();
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"直接引用文件夹请求失败：{response.Status} {await response.TextAsync()}");
        var modal = page.GetByRole(AriaRole.Dialog, new() { Name = "直接引用文件夹", Exact = true });
        await modal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await modal.Locator("tr.ant-table-row").Filter(new() { HasTextString = parentName }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await Assertions.Expect(modal.Locator(".ant-spin-spinning:visible")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
        await modal.Locator(".ant-modal-close").ClickAsync(new() { Trial = true });
        return modal;
    }

    internal async Task<ILocator> OpenReferencingFolderPropertiesAsync(ILocator modal, string parentName)
    {
        var row = modal.Locator("tr.ant-table-row").Filter(new() { HasTextString = parentName });
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/folders/", StringComparison.Ordinal)
            && new Uri(response.Url).AbsolutePath != "/api/workspace/folders/candidates");
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "打开属性", Exact = true }).ClickAsync();
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"直接引用父文件夹属性请求失败：{response.Status} {await response.TextAsync()}");
        return await WaitForFolderDrawerAsync(parentName);
    }

    internal async Task CloseDirectReferencesAsync(ILocator modal)
    {
        await modal.Locator(".ant-modal-close").ClickAsync();
        await modal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    /// <summary>通过当前文件夹的正式创建动作一次提交实体和关系。</summary>
    internal async Task CreateObjectAsync(string type, string code, string name, Func<ILocator, Task>? capture = null)
    {
        var route = type switch { "零组件" => "parts", "图文档" => "documents", _ => throw new ArgumentException("Unsupported object type.", nameof(type)) };
        var crud = new StandardCrudPageDriver(page);
        var drawer = await crud.OpenCreateDrawerAsync("新建" + type, "新建");
        await FillObjectCreationAsync(drawer, code, name);
        if (capture is not null) await capture(drawer);
        await crud.SaveAndCloseAsync(drawer, new("POST", "/api/workspace/content/" + route, 200, "新建" + type));
        await crud.WaitForRowAsync(name);
    }

    /// <summary>复用标准字段定位，等待必填字段就绪后交给统一 Drawer 保存驱动。</summary>
    internal async Task FillObjectCreationAsync(ILocator dialog, string code, string name)
    {
        await FillObjectFieldsAsync(dialog, code, name);
        await WaitForObjectSaveAsync(dialog);
    }

    // 带文件 Drawer 在 Stage 完成前禁用保存；字段填写不能提前等待保存按钮。
    private static async Task FillObjectFieldsAsync(ILocator dialog, string code, string name)
    {
        await StandardCrudPageDriver.FillDrawerFieldAsync(dialog, "编码", code);
        await StandardCrudPageDriver.FillDrawerFieldAsync(dialog, "名称", name);
    }

    private static Task WaitForObjectSaveAsync(ILocator dialog) =>
        Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = StandardCrudPageDriver.SaveAndCloseLabel, Exact = true }))
            .ToBeEnabledAsync(new() { Timeout = 30_000 });

    /// <summary>主文件与附件共用正式创建动作一次保存；内容语义在 API/IO 层验证。</summary>
    internal async Task CreateDocumentWithFilesAsync(string code, string name, FilePayload mainFile, Func<ILocator, Task>? capture = null, IReadOnlyList<FilePayload>? attachments = null)
    {
        var crud = new StandardCrudPageDriver(page);
        var drawer = await crud.OpenCreateDrawerAsync("新建图文档", "新建");
        await FillDocumentCreationAsync(drawer, code, name, mainFile, attachments);
        if (capture is not null) await capture(drawer);
        await crud.SaveAndCloseAsync(drawer, new("POST", "/api/workspace/content/documents", 200, "新建图文档"));
        await crud.WaitForRowAsync(name);
    }

    /// <summary>从可见选择按钮进入真实文件选择器，等待各文件状态后才交给统一保存。</summary>
    internal async Task FillDocumentCreationAsync(ILocator drawer, string code, string name, FilePayload mainFile, IReadOnlyList<FilePayload>? attachments = null)
    {
        await FillObjectFieldsAsync(drawer, code, name);
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath == "/api/workspace/content/document-uploads");
        var chooser = await page.RunAndWaitForFileChooserAsync(() =>
            drawer.GetByRole(AriaRole.Button, new() { Name = "选择主文件", Exact = true }).ClickAsync());
        await chooser.SetFilesAsync(mainFile);
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"主文件暂存请求失败：{response.Status} {await response.TextAsync()}");
        if (attachments is { Count: > 0 })
        {
            var attachmentChooser = await page.RunAndWaitForFileChooserAsync(() =>
                drawer.GetByRole(AriaRole.Button, new() { Name = "选择附件", Exact = true }).ClickAsync());
            await attachmentChooser.SetFilesAsync(attachments);
            foreach (var attachment in attachments)
                await drawer.GetByRole(AriaRole.Group, new() { Name = "附件 " + attachment.Name, Exact = true })
                    .GetByText("附件已上传，保存后生效。", new() { Exact = true })
                    .WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
        await WaitForObjectSaveAsync(drawer);
    }

    /// <summary>使用同一小文件走页面主文件路径，不在浏览器断言 hash、长度或持久状态。</summary>
    internal static FilePayload SmallMainFile() => new()
    {
        Name = "workspace-main.pdf", MimeType = "application/pdf",
        Buffer = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\nWorkspace acceptance main file\n%%EOF\n")
    };

    /// <summary>正式旅程只选两个小附件；上限、排序和字节断言由 API/SQL 层拥有。</summary>
    internal static IReadOnlyList<FilePayload> SmallAttachmentFiles() =>
    [
        new() { Name = "workspace-attachment-a.pdf", MimeType = "application/pdf", Buffer = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\nWorkspace attachment A\n%%EOF\n") },
        new() { Name = "workspace-attachment-b.pdf", MimeType = "application/pdf", Buffer = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\nWorkspace attachment B\n%%EOF\n") }
    ];

    /// <summary>从 Document 属性的正式下载按钮等待 attachment 下载；不回退到裸文件路径。</summary>
    internal async Task DownloadFileAsync(string fileName, string outputPath)
    {
        var row = page.GetByRole(AriaRole.Region, new() { Name = "文件", Exact = true }).GetByRole(AriaRole.Row)
            .Filter(new() { Has = page.GetByRole(AriaRole.Cell, new() { Name = fileName, Exact = true }) });
        var downloadTask = page.WaitForDownloadAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "下载", Exact = true }).ClickAsync();
        var download = await downloadTask;
        if (await download.FailureAsync() is { } failure) throw new InvalidOperationException("文件下载失败：" + failure);
        await download.SaveAsAsync(outputPath);
    }
    /// <summary>混合内容名称链接使用所属模块详情路由，不在操作菜单重复提供入口。</summary>
    internal async Task OpenOwnedPropertiesAsync(string name, string route)
    {
        var row = await new StandardCrudPageDriver(page).WaitForRowAsync(name);
        await row.GetByRole(AriaRole.Button, new() { Name = name, Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath.StartsWith(route.TrimEnd('/') + "/", StringComparison.Ordinal));
        await page.GetByText(name, new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await page.GetByText("基本信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await page.GetByText("审计信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    internal async Task ReturnToPublicContentAsync()
    {
        await page.GoBackAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/workspace");
        await SelectRootAsync("公共空间");
    }

    internal async Task<ILocator> OpenRemoveReferenceDialogAsync(string name)
    {
        var crud = new StandardCrudPageDriver(page);
        var row = await crud.WaitForRowAsync(name);
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "移除引用", Exact = true }).ClickAsync();
        var dialog = crud.ConfirmDialogWithText("仅从当前文件夹移除这条引用，保留对象及文件，确定继续？");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        return dialog;
    }

    internal async Task RemoveReferenceAsync(string name, Func<ILocator, Task>? capture = null)
    {
        var crud = new StandardCrudPageDriver(page);
        var dialog = await OpenRemoveReferenceDialogAsync(name);
        if (capture is not null) await capture(dialog);
        await crud.ConfirmAsync(dialog, new("DELETE", "/api/workspace/content/references/", 200, "移除引用", PathIsPrefix: true));
        await crud.TableRow(name).WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    internal Task<string> ReadPageDiagnosticAsync() => new StandardCrudPageDriver(page).ReadPageDiagnosticAsync();
}
