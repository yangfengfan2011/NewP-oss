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
        var drawer = await crud.WaitForVisibleDrawerAsync("编辑对象");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "名称", newName);
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "PUT"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/content/", StringComparison.Ordinal));
        await crud.SaveAndCloseWithoutResponseAsync(drawer);
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"编辑对象失败：{response.Status} {await response.TextAsync()}");
        await crud.WaitForRowAsync(newName);
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
        await StandardCrudPageDriver.FillDrawerFieldAsync(dialog, "编码", code);
        await StandardCrudPageDriver.FillDrawerFieldAsync(dialog, "名称", name);
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = StandardCrudPageDriver.SaveAndCloseLabel, Exact = true }))
            .ToBeEnabledAsync(new() { Timeout = 30_000 });
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
