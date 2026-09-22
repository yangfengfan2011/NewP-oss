using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

/// <summary>规则管理的真实操作路径；过滤和权限数值由低层 API 测试证明。</summary>
internal sealed class DataScopeRulesPageDriver(IPage page)
{
    /// <summary>从正式导航打开管理页，保存人员规则并等待实际服务端成功。</summary>
    internal async Task CreatePersonRuleAsync(string name)
    {
        await page.Locator(".np-sidebar-menu").GetByRole(AriaRole.Link, new() { Name = "数据范围规则", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "新增规则", Exact = true }).ClickAsync();
        await page.GetByLabel("规则名称", new() { Exact = true }).FillAsync(name);
        await SelectAsync("资源", "organization.person");
        await SelectAsync("直接属性", "ENAME");
        await page.GetByLabel("固定值", new() { Exact = true }).FillAsync("验收未命中人员");
        await SelectAsync("读取意见", "拒绝");
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "保存根规则", Exact = true }).ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.Contains("/api/authorization/data-scope/rules/", StringComparison.Ordinal));
        if (response.Status != 204) throw new InvalidOperationException($"Rule save returned {response.Status}.");
        await page.GetByText("规则已保存", new() { Exact = true }).WaitForAsync();
    }

    // 使用可访问名称操作 Ant Design 选项，不依赖模拟 DOM 或内存保存。
    private async Task SelectAsync(string label, string option)
    {
        var input = page.GetByRole(AriaRole.Combobox, new() { Name = label, Exact = true });
        await page.Locator(".ant-select").Filter(new() { Has = input }).Locator(".ant-select-selector").ClickAsync();
        // rc-select 的虚拟项只有进入窗口才挂载；用真实键盘导航触发其 scrollIntoView。
        var target = page.Locator(".ant-select-dropdown:visible .ant-select-item-option").Filter(new() { HasText = option });
        var navigation = System.Diagnostics.Stopwatch.StartNew();
        while (!await target.IsVisibleAsync())
        {
            if (navigation.Elapsed > TimeSpan.FromSeconds(10))
                throw new InvalidOperationException($"Select '{label}' has no reachable option '{option}'.");
            await input.PressAsync("ArrowDown");
        }
        await target.ClickAsync();
    }
}
