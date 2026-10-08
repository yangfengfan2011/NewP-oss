using Microsoft.Playwright;

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
        var responseTask = page.WaitForResponseAsync(response => response.Request.Method == "GET"
            && new Uri(response.Url).AbsolutePath.StartsWith("/api/workspace/spaces/roots/", StringComparison.Ordinal));
        await tree.GetByText(label, new() { Exact = true }).ClickAsync();
        var response = await responseTask;
        if (!response.Ok) throw new InvalidOperationException($"根详情请求失败：{response.Status} {await response.TextAsync()}");
        var content = page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true });
        await content.GetByRole(AriaRole.Heading, new() { Name = label, Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await content.GetByText("基本信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await content.GetByText("审计信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    internal Task<string> ReadPageDiagnosticAsync() => new StandardCrudPageDriver(page).ReadPageDiagnosticAsync();
}
