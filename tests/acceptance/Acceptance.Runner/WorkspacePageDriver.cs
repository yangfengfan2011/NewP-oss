using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace ICP.Platform.Acceptance;

/// <summary>工作空间唯一正式旅程的页面控件知识；后续内容操作继续扩展此驱动。</summary>
internal sealed class WorkspacePageDriver(IPage page)
{
    /// <summary>勾选真实混合资源，经批量输入配置完整主体项并只确认一次；载荷及事务语义由API测试承担。</summary>
    internal async Task SaveBatchAuthorizationAsync(string partName, string documentName, string presentationPath)
    {
        var crud = new StandardCrudPageDriver(page);
        foreach (var name in new[] { partName, documentName })
        {
            var row = await crud.WaitForRowAsync(name);
            await row.GetByRole(AriaRole.Checkbox).CheckAsync();
        }
        var preparing = page.WaitForResponseAsync(response => response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath == "/api/workspace/content/permissions/batch/prepare");
        await crud.ClickPageActionAsync("批量授权");
        var prepared = await preparing;
        if (!prepared.Ok) throw new InvalidOperationException($"批量授权准备失败：{prepared.Status} {await prepared.TextAsync()}");
        var dialog = crud.VisibleModal();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var sections = dialog.Locator("section");
        await sections.Nth(1).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        for (var index = 0; index < 2; index++)
        {
            await sections.Nth(index).GetByRole(AriaRole.Button, new() { Name = "添加主体项", Exact = true }).ClickAsync();
            var editor = crud.VisibleDrawer();
            await editor.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^确\s*定$") }).ClickAsync();
            await editor.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
            await sections.Nth(index).GetByRole(AriaRole.Checkbox, new() { Name = "读取：不选", Exact = true }).ClickAsync();
        }
        await AcceptancePresentation.CaptureAsync(page, presentationPath);
        await crud.ConfirmAsync(dialog, new("POST", "/api/workspace/content/permissions/batch", 200, "保存批量授权"));
        await crud.WaitForRowAsync(partName);
        await crud.WaitForRowAsync(documentName);
    }

    /// <summary>沿当前Part行的标准只读导航进入真实页签；保留原地址供后续回到同一来源。</summary>
    internal async Task<string> OpenPermissionViewerAsync(string rowName)
    {
        var address = page.Url;
        var row = await new StandardCrudPageDriver(page).WaitForRowAsync(rowName);
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        var loaded = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/content/", StringComparison.Ordinal)
            && new Uri(response.Url).AbsolutePath.EndsWith("/properties", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex(@"^权\s*限$") }).ClickAsync();
        var response = await loaded;
        if (!response.Ok) throw new InvalidOperationException($"来源属性读取失败：{response.Status} {await response.TextAsync()}");
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/workspace/permissions");
        await page.GetByRole(AriaRole.Tab, new() { Name = "属性", Exact = true }).WaitForAsync();
        await page.Locator(".np-crud-detail:visible").First.WaitForAsync();
        return address;
    }

    /// <summary>沿来源查看器的独立履历入口走通详情及选中输出；值、四种范围与权限边界由低层承担。</summary>
    internal async Task InspectObjectHistoryAndOutputAsync(string downloadPath)
    {
        var listLoaded = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/content/", StringComparison.Ordinal)
            && new Uri(response.Url).AbsolutePath.EndsWith("/history", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^履\s*历$") }).ClickAsync();
        var list = await listLoaded;
        if (!list.Ok) throw new InvalidOperationException($"操作履历读取失败：{list.Status}");
        var historyPath = new Uri(list.Url).AbsolutePath;
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/workspace/history");
        var row = page.Locator(".ant-table-tbody tr[data-row-key]").First;
        await row.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        var detailLoaded = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith(historyPath + "/", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex(@"^详\s*情$") }).ClickAsync();
        var detail = await detailLoaded;
        if (!detail.Ok) throw new InvalidOperationException($"操作履历详情读取失败：{detail.Status}");
        var drawer = new StandardCrudPageDriver(page).VisibleDrawer();
        await drawer.Locator(".ant-descriptions").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await drawer.Locator(".ant-drawer-close").ClickAsync();
        await drawer.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await row.GetByRole(AriaRole.Checkbox).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^输\s*出$") }).ClickAsync();
        var output = new StandardCrudPageDriver(page).VisibleModal();
        await output.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var downloaded = page.WaitForDownloadAsync();
        await output.GetByRole(AriaRole.Button, new() { Name = "导出选中", Exact = true }).ClickAsync();
        var download = await downloaded;
        if (await download.FailureAsync() is { } failure) throw new InvalidOperationException("履历下载失败：" + failure);
        await download.SaveAsAsync(downloadPath);
        var printLoaded = page.WaitForResponseAsync(response => response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath == historyPath + "/print");
        await output.GetByRole(AriaRole.Button, new() { Name = "打印选中", Exact = true }).ClickAsync();
        var print = await printLoaded;
        if (!print.Ok) throw new InvalidOperationException($"操作履历打印数据读取失败：{print.Status}");
        await output.Locator("table").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await output.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^打\s*印$") }).ClickAsync();
        await output.Locator(".ant-modal-footer").GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^关\s*闭$") }).ClickAsync();
        await output.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await page.Locator("iframe[title=\"操作履历打印\"]").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    /// <summary>只证明用户能够切到解释页；名称和裁决语义由正式API测试承担。</summary>
    internal async Task InspectPermissionTabAsync()
    {
        var loaded = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.EndsWith("/permissions/explanation", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Tab, new() { Name = "权限", Exact = true }).ClickAsync();
        var response = await loaded;
        if (!response.Ok) throw new InvalidOperationException($"对象权限解释读取失败：{response.Status} {await response.TextAsync()}");
        await page.GetByText("分析用户", new() { Exact = true }).First.WaitForAsync();
    }

    /// <summary>通过现管理动作编辑真实草稿，再经唯一根保存提交；不直接构造授权载荷。</summary>
    internal async Task AddReadPermissionAndSaveAsync(string rowName)
    {
        var crud = new StandardCrudPageDriver(page);
        var row = await crud.WaitForRowAsync(rowName);
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        var configuration = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/content/", StringComparison.Ordinal)
            && new Uri(response.Url).AbsolutePath.EndsWith("/permissions", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "对象授权", Exact = true }).ClickAsync();
        var loaded = await configuration;
        if (!loaded.Ok) throw new InvalidOperationException($"对象授权装载失败：{loaded.Status} {await loaded.TextAsync()}");
        var dialog = crud.VisibleModal();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "添加主体项", Exact = true }).ClickAsync();
        var editor = crud.VisibleDrawer();
        await editor.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^确\s*定$") }).ClickAsync();
        await editor.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await dialog.GetByRole(AriaRole.Checkbox, new() { Name = "读取：不选", Exact = true }).ClickAsync();
        await crud.ConfirmAsync(dialog, new("PUT", new Uri(loaded.Url).AbsolutePath, 204, "保存对象权限"));
        await crud.WaitForRowAsync(rowName);
    }

    /// <summary>通过 Shell 的真实语言控件选择语言；只供原 Native 详情呈现核对使用。</summary>
    internal async Task SelectLanguageAsync(string language)
    {
        await page.Locator(".np-shell-language-select .ant-select-selector").ClickAsync();
        await page.Locator(".ant-select-dropdown:visible").GetByText(language, new() { Exact = true }).ClickAsync();
    }

    /// <summary>归属维护复用实际列表工具栏或行菜单；只等待真实输入面板，不在浏览器裁决归属值。</summary>
    internal async Task<ILocator> OpenOwnershipInitializationAsync(string? rowName = null, bool english = false)
    {
        var crud = new StandardCrudPageDriver(page);
        if (rowName is null) await crud.ClickPageActionAsync(english ? "Missing ownership" : "待补齐归属");
        else
        {
            var row = await crud.WaitForRowAsync(rowName);
            await row.GetByRole(AriaRole.Button, new() { Name = english ? "More" : "更多", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = english ? "Initialize ownership" : "补齐归属", Exact = true }).ClickAsync();
        }
        var dialog = crud.VisibleModal();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        return dialog;
    }

    /// <summary>人工补齐通过正式可搜索候选选中账号，不向页面注入ID或替代写入。</summary>
    internal async Task ChooseOwnershipAccountAsync(ILocator dialog, string query)
    {
        await dialog.GetByRole(AriaRole.Radio, new() { Name = "人工指定账号", Exact = true }).CheckAsync();
        var input = dialog.GetByRole(AriaRole.Combobox);
        var loaded = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath == "/api/workspace/ownership/accounts"
            && StandardCrudPageDriver.HasQueryValue(new Uri(response.Url), "query", query));
        await input.FillAsync(query);
        var response = await loaded;
        if (!response.Ok) throw new InvalidOperationException($"归属账号候选请求失败：{response.Status} {await response.TextAsync()}");
        var candidate = page.Locator(".ant-select-dropdown:visible .ant-select-item-option:not(.ant-select-item-option-disabled)")
            .Filter(new() { HasTextString = query }).First;
        await candidate.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await candidate.ClickAsync();
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

    /// <summary>按完整可见路径收窄同名搜索结果，点击后等待正式位置重验与内容交接。</summary>
    internal async Task<string> SearchAndSelectFolderAsync(string keyword, string[] labels, Func<Task>? capture = null)
    {
        var input = page.GetByRole(AriaRole.Textbox, new() { Name = "搜索节点", Exact = true });
        // 清空后重新输入也覆盖再次搜索同一词，不依赖旧浮层是否仍处于焦点。
        await input.FillAsync("");
        var searched = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath == "/api/trees/workspace.structure/search");
        await input.FillAsync(keyword);
        var response = await searched;
        if (!response.Ok) throw new InvalidOperationException($"文件夹搜索失败：{response.Status} {await response.TextAsync()}");
        await response.FinishedAsync();
        var result = page.Locator("[aria-label='搜索结果']")
            .GetByRole(AriaRole.Button, new() { Name = "定位节点 " + labels[^1], Exact = true })
            .Filter(new() { Has = page.GetByText(string.Join(" / ", labels), new() { Exact = true }) });
        await Assertions.Expect(result).ToHaveCountAsync(1);
        await Assertions.Expect(result).ToBeVisibleAsync();
        if (capture is not null) await capture();
        var resolved = page.WaitForResponseAsync(reply => reply.Request.Method == "GET"
            && new Uri(reply.Url).AbsolutePath == "/api/trees/workspace.structure/path");
        await result.ClickAsync();
        var path = await resolved;
        if (!path.Ok) throw new InvalidOperationException($"搜索结果定位失败：{path.Status} {await path.TextAsync()}");
        await WaitForContentAsync(labels[^1]);
        await new StandardTreePageDriver(page).WaitForBreadcrumbAsync(labels);
        return page.Url;
    }

    // rc-tree 5.13以class呈现选中态而未输出aria-selected；按实际选中出现收窄，不能用First猜父链。
    private ILocator FolderTreeNode(string name, bool selectedOnly)
    {
        var nodes = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true })
            .GetByRole(AriaRole.Treeitem, new() { Name = name, Exact = true });
        if (selectedOnly) nodes = nodes.And(page.Locator(".ant-tree-treenode-selected"));
        return nodes.GetByText(name, new() { Exact = true });
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

    internal async Task CreateFolderAsync(string name, bool? copyParentAuthorization = null)
    {
        var crud = new StandardCrudPageDriver(page);
        var drawer = await crud.OpenCreateDrawerAsync("新建文件夹", "新建");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "名称", name);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "说明", "工作空间正式创建旅程");
        await SelectParentAuthorizationAsync(drawer, copyParentAuthorization);
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
    internal async Task ReorderFolderAsync(string name, string direction, Func<Task>? capture = null, bool selectedOnly = false)
    {
        var label = direction switch { "up" => "上移", "down" => "下移", _ => throw new ArgumentException("Unsupported tree direction.", nameof(direction)) };
        await FolderTreeNode(name, selectedOnly).ClickAsync(new() { Button = MouseButton.Right });
        await WaitForContentAsync(name);
        var action = page.GetByRole(AriaRole.Menuitem, new() { Name = label, Exact = true });
        await action.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await CaptureContextMenuAsync(capture);
        var written = false;
        var treeRead = page.WaitForResponseAsync(response => written && response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath == "/api/trees/workspace.structure/children");
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
    internal async Task CopyFolderFromTreeContextMenuAsync(string name, Func<Task>? capture = null, bool selectedOnly = false)
    {
        await FolderTreeNode(name, selectedOnly).ClickAsync(new() { Button = MouseButton.Right });
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
        var selectAll = page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true }).Locator("thead").GetByRole(AriaRole.Checkbox);
        await selectAll.CheckAsync(); await selectAll.UncheckAsync();
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

    /// <summary>从正式名称输入提交浅副本，只证明用户能确认并在目标看到结果。</summary>
    internal Task PasteFolderCopyAsync(string name, Func<Task>? capture = null, bool? copyParentAuthorization = null)
        => PasteNamedFolderAsync("粘贴文件夹副本", "copy", name, capture, copyParentAuthorization);

    /// <summary>结构副本使用自己的正式动作和端点，结果数据语义由 SQL 测试验证。</summary>
    internal Task PasteFolderStructureAsync(string name, Func<Task>? capture = null, bool? copyParentAuthorization = null)
        => PasteNamedFolderAsync("粘贴文件夹结构", "copy-structure", name, capture, copyParentAuthorization);

    /// <summary>两种复制共用名称输入交接；异步准备完成后填写，提交后消费正式列表刷新。</summary>
    private async Task PasteNamedFolderAsync(string actionLabel, string endpoint, string name, Func<Task>? capture, bool? copyParentAuthorization)
    {
        var crud = new StandardCrudPageDriver(page);
        await crud.ClickPageActionAsync(actionLabel);
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = actionLabel, Exact = true });
        await dialog.GetByRole(AriaRole.Textbox, new() { Name = "新文件夹名称", Exact = true }).FillAsync(name);
        await SelectParentAuthorizationAsync(dialog, copyParentAuthorization);
        if (capture is not null) await capture();
        var written = page.WaitForResponseAsync(response => response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath == "/api/workspace/content/folders/" + endpoint);
        await dialog.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^确\s*认$") }).ClickAsync();
        var response = await written;
        if (!response.Ok) throw new InvalidOperationException($"{actionLabel}提交失败：{response.Status} {await response.TextAsync()}");
        await Assertions.Expect(dialog).ToHaveCountAsync(0);
        await crud.WaitForRowAsync(name);
    }

    // 只在本次旅程明确选择时操作真实控件，保留旧driver调用的交互顺序。
    private static async Task SelectParentAuthorizationAsync(ILocator surface, bool? selected)
    {
        if (selected is not null)
            await surface.GetByRole(AriaRole.Checkbox, new() { Name = "复制父文件夹直接授权", Exact = true }).SetCheckedAsync(selected.Value);
    }

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
    internal async Task CutFolderFromTreeContextMenuAsync(string name, Func<Task>? capture = null, bool selectedOnly = false)
    {
        await FolderTreeNode(name, selectedOnly).ClickAsync(new() { Button = MouseButton.Right });
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
        await page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true })
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^移\s*除$") }).ClickAsync();
        var dialog = crud.ConfirmDialogWithText("仅从当前文件夹移除所选引用，保留对象及文件，确定继续？");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        if (capture is not null)
        {
            // 可见边框早于Modal入场完成，只有呈现采样需要等实际淡入状态。
            await Assertions.Expect(dialog).ToHaveCSSAsync("opacity", "1");
            await dialog.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
            await capture(dialog);
        }
        await crud.ConfirmAsync(dialog, new("POST", "/api/workspace/content/references/remove", 200, "移除"));
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
        await page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true })
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^删\s*除$") }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "删除", Exact = true });
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

    /// <summary>文件夹名称用于定位树；只读属性从内容行的更多菜单打开，继续使用真实实体读取。</summary>
    internal async Task<ILocator> OpenFolderPropertiesAsync(string name)
    {
        var crud = new StandardCrudPageDriver(page);
        var row = await crud.WaitForRowAsync(name);
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/folders/", StringComparison.Ordinal)
            && new Uri(response.Url).AbsolutePath != "/api/workspace/folders/candidates");
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "打开属性", Exact = true }).ClickAsync();
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

    /// <summary>通过正式副本确认填写新编码并等待目标列表消费成功结果。</summary>
    internal async Task PastePartCopyAsync(string code, string name, Func<ILocator, Task>? capture = null)
        => await PasteObjectCopyAsync(code, name, capture);

    /// <summary>Part和Document共用正式副本确认及保存驱动，等待目标列表消费结果。</summary>
    internal async Task PasteObjectCopyAsync(string code, string name, Func<ILocator, Task>? capture = null)
    {
        var crud = new StandardCrudPageDriver(page);
        var drawer = await OpenObjectCopyAsync();
        await FillObjectCreationAsync(drawer, code, name);
        if (capture is not null) await capture(drawer);
        await crud.SaveAndCloseAsync(drawer, new("POST", "/api/workspace/content/objects/copy", 200, "粘贴对象真副本"));
        await crud.WaitForRowAsync(name);
    }

    /// <summary>集合每个来源使用自己的正式区域和字段；保存后消费安全结果弹层与目标列表。</summary>
    internal async Task PasteDocumentCopiesAsync(IReadOnlyList<(string SourceName, string Code, string Name)> rows, Func<ILocator, Task>? capture = null,
        Func<Task>? captureResult = null)
    {
        var crud = new StandardCrudPageDriver(page); var drawer = await OpenDocumentCopiesAsync();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = drawer.GetByRole(AriaRole.Region, new() { Name = $"{index + 1}. {rows[index].SourceName}", Exact = true });
            await FillObjectFieldsAsync(row, rows[index].Code, rows[index].Name);
        }
        await WaitForObjectSaveAsync(drawer);
        if (capture is not null) await capture(drawer);
        await crud.SaveAndCloseAsync(drawer, new("POST", "/api/workspace/content/objects/copy", 200, "粘贴图文档集合副本"));
        var result = page.GetByRole(AriaRole.Dialog, new() { Name = "对象副本结果", Exact = true });
        await result.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        if (captureResult is not null)
        {
            // 可交互visible早于Modal入场完成；呈现只等待实际opacity/动画，不用固定延时。
            await Assertions.Expect(result).ToHaveCSSAsync("opacity", "1");
            await result.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
            await captureResult();
        }
        // Ant的双中文按钮有可访问空格，使用与既有确认驱动相同的精确角色/文本规则。
        await result.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^关\s*闭$") }).ClickAsync();
        await result.WaitForAsync(new() { State = WaitForSelectorState.Detached });
        foreach (var row in rows) await crud.WaitForRowAsync(row.Name);
    }

    /// <summary>集合确认等待真实异步字段就绪，不用单对象strict locator或只提交首行。</summary>
    internal async Task<ILocator> OpenDocumentCopiesAsync(bool english = false)
    {
        var drawer = await new StandardCrudPageDriver(page).OpenCreateDrawerAsync(english ? "Paste independent object copy" : "粘贴对象副本");
        await Assertions.Expect(drawer.GetByLabel(english ? "Code" : "编码", new() { Exact = true }).First).ToBeEnabledAsync();
        return drawer;
    }

    /// <summary>父Gate和定向冒烟复用同一集合片段：准备第二源、逐行确认、详情下载与编辑删除；按需补语言/窄容器呈现。</summary>
    internal async Task DocumentBatchCopyJourneyAsync(string sourceAddress, string sourceFolderName, string targetFolderName,
        string firstCode, string firstName, string downloadPath, Func<string, Task> capture)
    {
        var secondName = firstName + " 第二份"; var firstCopy = firstName + " 独立副本"; var secondCopy = firstName + " 第二份副本";
        await RestorePositionAsync(sourceAddress, "公共空间", sourceFolderName);
        await CreateDocumentWithFilesAsync(firstCode + "-B", secondName, SmallMainFile(), AcceptancePresentation.Enabled ? _ => capture("document-batch-create-reference.png") : null, SmallAttachmentFiles());
        await CopySelectedAsync(firstName, secondName);
        var targetAddress = await SelectTreePathAsync("公共空间", targetFolderName);
        await PasteDocumentCopiesAsync([(firstName, firstCode + "-COPY-A", firstCopy), (secondName, firstCode + "-COPY-B", secondCopy)],
            AcceptancePresentation.Enabled ? async drawer => {
                await capture("document-batch-input-zhs.png"); await page.SetViewportSizeAsync(900, 1000); await capture("document-batch-input-zhs-narrow.png");
                await drawer.Locator(".ant-drawer-body").EvaluateAsync("node => node.scrollTop = node.scrollHeight"); await capture("document-batch-input-zhs-narrow-bottom.png");
                await page.SetViewportSizeAsync(1440, 1000);
            } : null, AcceptancePresentation.Enabled ? () => capture("document-batch-result.png") : null);
        if (AcceptancePresentation.Enabled)
        {
            await SelectLanguageAsync("English"); var english = await OpenDocumentCopiesAsync(english: true);
            await capture("document-batch-input-en.png"); await page.SetViewportSizeAsync(900, 1000); await capture("document-batch-input-en-narrow.png");
            await english.Locator(".ant-drawer-body").EvaluateAsync("node => node.scrollTop = node.scrollHeight"); await capture("document-batch-input-en-narrow-bottom.png");
            await page.SetViewportSizeAsync(1440, 1000); await new StandardCrudPageDriver(page).CloseDrawerAsync(english); await SelectLanguageAsync("简体中文");
        }
        await OpenOwnedPropertiesAsync(firstCopy, "/documents"); await DownloadFileAsync(SmallMainFile().Name, downloadPath);
        if (AcceptancePresentation.Enabled) await capture("document-batch-properties.png");
        await RestorePositionAsync(targetAddress, "公共空间", targetFolderName); await EditObjectAsync(firstCopy, firstCopy + " 已编辑");
        await DeleteSelectedAsync(firstCopy + " 已编辑", secondCopy);
        await RestorePositionAsync(sourceAddress, "公共空间", sourceFolderName); await DeleteSelectedAsync(secondName);
    }
    /// <summary>副本输入等待真实异步来源准备；语言仅改变正式可访问名称。</summary>
    internal async Task<ILocator> OpenObjectCopyAsync(bool english = false)
    {
        var drawer = await new StandardCrudPageDriver(page).OpenCreateDrawerAsync(english ? "Paste independent object copy" : "粘贴对象副本");
        await Assertions.Expect(drawer.GetByLabel(english ? "Code" : "编码", new() { Exact = true })).ToBeEnabledAsync();
        return drawer;
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
