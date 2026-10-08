using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

/// <summary>零组件与图文档共同采用的标准创建、只读属性和独立详情控件知识。</summary>
internal sealed class StandardMetadataPageDriver(IPage page, string navigationTitle, string listPath, string apiPath, string createTitle)
{
    private readonly StandardCrudPageDriver _crud = new(page);

    internal async Task OpenAsync()
    {
        await _crud.Page.Locator(".np-sidebar-menu").GetByRole(AriaRole.Link, new() { Name = navigationTitle, Exact = true }).ClickAsync();
        await _crud.Page.WaitForURLAsync(url => new Uri(url).AbsolutePath == listPath);
        await _crud.Page.Locator(".ant-table").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await Assertions.Expect(_crud.Page.Locator(".ant-spin-spinning:visible")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
    }

    internal Task<ILocator> OpenCreateAsync() => _crud.OpenCreateDrawerAsync(createTitle);

    internal async Task SaveCreateAsync(ILocator drawer, string code, string name)
    {
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "编码", code);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "名称", name);
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "说明", navigationTitle + "正式创建旅程");
        await _crud.SaveAndCloseAsync(drawer, new("POST", apiPath, 201, createTitle));
        await _crud.WaitForRowAsync(code, name);
    }

    internal async Task<ILocator> OpenPropertiesAsync(string code, string name)
    {
        var row = await _crud.WaitForRowAsync(code, name);
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        var responseTask = _crud.Page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith(apiPath.TrimEnd('/') + "/", StringComparison.Ordinal));
        await _crud.Page.GetByRole(AriaRole.Menuitem, new() { Name = "打开属性", Exact = true }).ClickAsync();
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"{navigationTitle}属性请求失败：{response.Status} {await response.TextAsync()}");
        var drawer = await _crud.WaitForVisibleDrawerAsync(navigationTitle + "属性");
        await drawer.GetByText(name, new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await drawer.GetByText("基本信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await drawer.GetByText("审计信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        return drawer;
    }

    internal async Task OpenFullDetailAsync(ILocator drawer, string name)
    {
        await drawer.GetByRole(AriaRole.Button, new() { Name = "查看完整详情", Exact = true }).ClickAsync();
        await _crud.Page.WaitForURLAsync(url => new Uri(url).AbsolutePath.StartsWith(listPath.TrimEnd('/') + "/", StringComparison.Ordinal));
        await _crud.Page.GetByText(name, new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await _crud.Page.GetByText("基本信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await _crud.Page.GetByText("审计信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }
}
