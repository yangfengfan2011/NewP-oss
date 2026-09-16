using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

// 页面驱动（功能授权工作台组）：持有工作台控件、局部等待和保存响应知识，不持有角色准备、旅程顺序或性能计时。
internal sealed class AuthorizationWorkbenchPageDriver
{
    private const string WorkbenchPath = "/api/authorization/workbench";
    private readonly IPage _page;

    internal AuthorizationWorkbenchPageDriver(IPage page)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
    }

    // 只触发 Shell 中的正式工作台入口；响应和可操作终点由调用方按场景等待。
    internal Task ClickSidebarEntryAsync() =>
        _page.Locator(".np-sidebar-menu")
            .GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "功能授权", Exact = true })
            .ClickAsync();

    // 规模观察的入口终点是首个角色按钮可见，不附加当前角色或全页就绪条件。
    internal Task WaitForFirstRoleVisibleAsync() =>
        _page.Locator(".ant-list-item button").First.WaitForAsync();

    // 分页动作只持有控件定位；精确页码响应仍由规模观察场景等待。
    internal Task ClickNextRolePageAsync() => _page.Locator(".ant-pagination-next").ClickAsync();

    // 分页动作只持有控件定位；精确页码响应仍由规模观察场景等待。
    internal Task ClickPreviousRolePageAsync() => _page.Locator(".ant-pagination-prev").ClickAsync();

    // 提交角色搜索；调用方可在动作前登记自己的精确响应观察。
    internal async Task SubmitRoleSearchAsync(string query)
    {
        var search = _page.GetByLabel("搜索角色", new PageGetByLabelOptions { Exact = true });
        await search.FillAsync(query);
        await search.PressAsync("Enter");
    }

    // 点击名称和代码共同标识的角色，避免同名角色命中错误。
    internal Task ClickRoleAsync(string name, string code) =>
        RoleButton(name, code).ClickAsync();

    // 当前角色加载完成以唯一工作台卡片标题为准。
    internal Task WaitForCurrentRoleTitleAsync(string name, string code) =>
        _page.GetByText(RoleLabel(name, code), new PageGetByTextOptions { Exact = true })
            .And(_page.Locator(".ant-card-head-title"))
            .WaitForAsync();

    // Personnel 呈现观察只要求目标角色按钮恢复可操作，不扩大为其他工作台就绪条件。
    internal Task WaitForRoleButtonEnabledAsync(string name, string code) =>
        Assertions.Expect(RoleButton(name, code)).ToBeEnabledAsync();

    // 展开当前筛选结果；渲染稳定或性能采样终点仍由调用方决定。
    internal Task ExpandAllAsync() =>
        _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "展开全部", Exact = true }).ClickAsync();

    // 功能搜索只改变当前可见树，查询后的呈现或截图由调用方拥有。
    internal Task SearchFeaturesAsync(string query) =>
        _page.GetByLabel("搜索功能", new PageGetByLabelOptions { Exact = true }).FillAsync(query);

    // Authorization 冒烟只触发首个可见功能勾选，不把数据集合语义上移到浏览器层。
    internal Task ClickFirstVisibleFeatureCheckboxAsync() =>
        _page.Locator(".ant-tree-checkbox").First.ClickAsync();

    // 保存同时验证精确写入响应和既有用户反馈；调用方继续拥有后续等待顺序。
    internal async Task SaveAsync(string roleId)
    {
        var path = $"{WorkbenchPath}/roles/{Uri.EscapeDataString(roleId)}";
        var responseTask = _page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "PUT", StringComparison.Ordinal)
            && string.Equals(new Uri(response.Url).AbsolutePath, path, StringComparison.Ordinal));

        await _page.GetByRole(
                AriaRole.Button,
                new PageGetByRoleOptions { Name = "保存授权", Exact = true })
            .ClickAsync();
        var response = await responseTask;
        if (response.Status != 204)
        {
            throw new InvalidOperationException(
                $"Browser role feature save returned {response.Status}, expected 204: {await response.TextAsync()}");
        }

        await _page.GetByText("授权已保存", new PageGetByTextOptions { Exact = true }).WaitForAsync();
    }

    // 等待原有提交 loading 与 Spin 呈现结束，不附加其他工作台就绪条件。
    internal Task WaitForSavePresentationFinishedAsync() =>
        _page.Locator(".ant-btn-loading, .ant-spin-spinning")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

    // 名称和代码共同组成工作台角色按钮的可访问名称。
    private ILocator RoleButton(string name, string code) =>
        _page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = RoleLabel(name, code), Exact = true });

    private static string RoleLabel(string name, string code) => $"{name} · {code}";
}
