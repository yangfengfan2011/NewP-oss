using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace ICP.Platform.Acceptance;

/// <summary>工作空间唯一正式旅程的页面控件知识；后续内容操作继续扩展此驱动。</summary>
internal sealed class WorkspacePageDriver(IPage page)
{
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
        var drawer = await crud.OpenCreateDrawerAsync("新建文件夹");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "名称", name);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "说明", "工作空间正式创建旅程");
        await crud.SaveAndCloseAsync(drawer, new("POST", "/api/workspace/content/folders", null, "创建子文件夹"));
        await crud.WaitForRowAsync(name);
    }

    internal async Task ExpandRootToFolderAsync(string rootLabel, string folderName)
    {
        var tree = page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
        var root = tree.Locator(".ant-tree-treenode[aria-expanded]").Filter(new() { HasTextString = rootLabel }).First;
        await root.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        if (await root.GetAttributeAsync("aria-expanded") != "true") await root.Locator(".ant-tree-switcher").ClickAsync();
        await tree.GetByText(folderName, new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    internal async Task<ILocator> OpenFolderPropertiesAsync(string name)
    {
        var crud = new StandardCrudPageDriver(page);
        var row = await crud.WaitForRowAsync(name);
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/folders/", StringComparison.Ordinal)
            && new Uri(response.Url).AbsolutePath != "/api/workspace/folders/candidates");
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

    internal async Task<ILocator> OpenAddExistingObjectDialogAsync()
    {
        var crud = new StandardCrudPageDriver(page);
        await crud.ClickPageActionAsync("添加已有对象");
        var dialog = crud.VisibleModal();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        return dialog;
    }

    internal Task AddExistingFolderAsync(string name, Func<ILocator, Task>? capture = null) =>
        AddExistingObjectAsync("文件夹", name, name, capture);

    internal async Task AddExistingObjectAsync(string type, string name, string optionLabel, Func<ILocator, Task>? capture = null)
    {
        var crud = new StandardCrudPageDriver(page);
        var dialog = await OpenAddExistingObjectDialogAsync();
        await SelectObjectCandidateAsync(dialog, type, name, optionLabel);
        if (capture is not null) await capture(dialog);
        await crud.ConfirmAsync(dialog, new("POST", "/api/workspace/content/references", 200, "添加已有对象"));
        await crud.WaitForRowAsync(name);
    }

    internal async Task SelectObjectCandidateAsync(ILocator dialog, string type, string keyword, string optionLabel)
    {
        // Ant Select 的 readonly input 会被已选值覆盖；通过标签定位字段，再点击正式 selector。
        var typeField = dialog.Locator(".ant-form-item").Filter(new()
            { Has = page.GetByRole(AriaRole.Combobox, new() { Name = "对象类型", Exact = true }) });
        await typeField.Locator(".ant-select-selector").ClickAsync();
        await page.Locator(".ant-select-dropdown:visible").Last.GetByText(type, new() { Exact = true }).ClickAsync();
        var input = dialog.GetByRole(AriaRole.Combobox, new() { NameRegex = new Regex(@"^\*\s*对象$") });
        var objectField = dialog.Locator(".ant-form-item").Filter(new()
            { Has = page.GetByRole(AriaRole.Combobox, new() { NameRegex = new Regex(@"^\*\s*对象$") }) });
        await objectField.Locator(".ant-select-selector").ClickAsync();
        await input.FillAsync(keyword);
        await page.Locator(".ant-select-dropdown:visible").Last.GetByText(optionLabel, new() { Exact = true }).ClickAsync();
        await Assertions.Expect(objectField.Locator(".ant-select-selection-item"))
            .ToHaveTextAsync(optionLabel, new() { Timeout = 30_000 });
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^确\s*认$") }))
            .ToBeEnabledAsync(new() { Timeout = 30_000 });
    }

    internal async Task OpenOwnedPropertiesAsync(string name, string route)
    {
        var row = await new StandardCrudPageDriver(page).WaitForRowAsync(name);
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "打开属性", Exact = true }).ClickAsync();
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
        var dialog = crud.ConfirmDialogWithText("移除当前文件夹中的这条引用？");
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
