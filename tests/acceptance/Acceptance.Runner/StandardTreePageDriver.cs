using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

/// <summary>Workspace 与 Organization 共享标准树的可见路径和节点面包屑控件知识。</summary>
internal sealed class StandardTreePageDriver(IPage page)
{
    private ILocator Tree => page.GetByRole(AriaRole.Complementary, new() { Name = "结构树", Exact = true });
    private ILocator Content => page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true });
    private ILocator Breadcrumb => Content.GetByRole(AriaRole.Navigation, new() { Name = "面包屑", Exact = true });

    // 调用方只展开一条可见分支；重复名称不通过 First 猜测另一出现位置。
    internal async Task SelectPathAsync(params string[] labels)
    {
        foreach (var label in labels[..^1])
        {
            var node = Tree.GetByRole(AriaRole.Treeitem, new() { Name = label, Exact = true });
            await node.WaitForAsync(new() { State = WaitForSelectorState.Visible });
            if (await node.GetAttributeAsync("aria-expanded") != "true") await node.Locator(".ant-tree-switcher").ClickAsync();
        }
        await Tree.GetByText(labels[^1], new() { Exact = true }).ClickAsync();
        await Content.GetByRole(AriaRole.Heading, new() { Name = labels[^1], Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await WaitForBreadcrumbAsync(labels);
    }

    internal async Task WaitForBreadcrumbAsync(params string[] labels)
    {
        await Breadcrumb.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await Assertions.Expect(Breadcrumb.GetByRole(AriaRole.Button)).ToHaveTextAsync(labels[..^1], new() { Timeout = 30_000 });
        await Breadcrumb.GetByText(labels[^1], new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    internal Task ClickAncestorAsync(string label) => Breadcrumb.GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync();
}
