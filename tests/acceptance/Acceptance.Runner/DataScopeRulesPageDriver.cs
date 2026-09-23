using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

internal sealed record DataScopeDepartmentSelection(string SearchText, string DisplayName);

/// <summary>规则管理的真实操作路径；过滤和权限数值由低层 API 测试证明。</summary>
internal sealed class DataScopeRulesPageDriver(IPage page)
{
    private const string ChildRuleName = "人员子例外";
    private const string SiblingRuleName = "人员旁支";

    /// <summary>在同一根中选择真实角色主体、显式优先级与当前账号条件，等待服务端保存和页面恢复。</summary>
    internal async Task CreatePersonRuleAsync(string name, string roleName,
        DataScopeDepartmentSelection? department = null, Func<Task>? captureDepartmentCandidates = null,
        bool includeCompositeCondition = false, bool includeRuleTree = false,
        Func<Task>? captureDraftGuard = null)
    {
        await page.Locator(".np-sidebar-menu").GetByRole(AriaRole.Link, new() { Name = "数据范围规则", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "新增规则", Exact = true }).ClickAsync();
        await page.GetByLabel("规则名称", new() { Exact = true }).FillAsync(name);
        await SelectAsync("资源", "organization.person");
        await SelectAsync("直接属性", "AccountEID");
        await SelectAsync("值来源", "当前身份值");
        await SelectAsync("当前身份值", "当前账号");
        if (includeCompositeCondition) await AddCompositeConditionAsync();
        var everyone = page.GetByRole(AriaRole.Group, new() { Name = "主体授权项 1", Exact = true });
        await SelectAsync("读取意见", includeRuleTree ? "拒绝" : "同意", everyone);
        await page.GetByRole(AriaRole.Button, new() { Name = "添加主体项", Exact = true }).ClickAsync();
        var role = page.GetByRole(AriaRole.Group, new() { Name = "主体授权项 2", Exact = true });
        await SelectAsync("主体类型", "角色", role);
        await SelectAsync("主体选择", roleName, role, search: true);
        var priority = role.GetByRole(AriaRole.Spinbutton, new() { Name = "优先级", Exact = true });
        await priority.FillAsync("10");
        await priority.PressAsync("Tab");
        await SelectAsync("读取意见", "同意", role);
        if (department is not null)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "添加主体项", Exact = true }).ClickAsync();
            var organization = page.GetByRole(AriaRole.Group, new() { Name = "主体授权项 3", Exact = true });
            await SelectAsync("主体类型", "部门", organization);
            await SelectAsync("主体选择", department.DisplayName, organization, search: true,
                searchText: department.SearchText, candidatesReady: captureDepartmentCandidates);
            await SelectAsync("读取意见", "同意", organization);
        }
        if (includeRuleTree) await AddRuleTreeAsync(name, captureDraftGuard);
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "保存根规则", Exact = true }).ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.Contains("/api/authorization/data-scope/rules/", StringComparison.Ordinal));
        if (response.Status != 204) throw new InvalidOperationException($"Rule save returned {response.Status}.");
        await page.GetByText("规则已保存", new() { Exact = true }).WaitForAsync();
        await Assertions.Expect(page.GetByLabel("规则名称", new() { Exact = true })).ToBeEnabledAsync();
    }

    /// <summary>重新装载已保存根并等待最小目录解析完成；只确认用户可识别并继续编辑已有主体。</summary>
    internal async Task ReloadRuleAsync(string name, string roleName, DataScopeDepartmentSelection department,
        bool expectCompositeCondition = false, bool expectRuleTree = false)
    {
        await page.ReloadAsync();
        await page.Locator(".ant-tree-title").Filter(new() { HasText = name }).ClickAsync();
        await page.GetByTitle(roleName, new() { Exact = true }).WaitForAsync();
        await page.GetByTitle(department.DisplayName, new() { Exact = true }).WaitForAsync();
        if (expectCompositeCondition)
        {
            await page.GetByRole(AriaRole.Group, new() { Name = "条件项 1.2.1", Exact = true }).WaitForAsync();
            await page.GetByRole(AriaRole.Group, new() { Name = "条件项 1.2.2", Exact = true })
                .GetByText("无读取贡献；完整判断仍生效", new() { Exact = true }).WaitForAsync();
        }
        if (expectRuleTree)
        {
            await page.Locator(".ant-tree-title").Filter(new() { HasText = ChildRuleName }).ClickAsync();
            await page.GetByText($"当前节点：{ChildRuleName}", new() { Exact = true }).WaitForAsync();
            await page.GetByText("完整条件还包含 1 个祖先条件", new() { Exact = true }).WaitForAsync();
            await page.GetByText($"当前根树：{name}", new() { Exact = true }).WaitForAsync();
            await page.Locator(".ant-tree-title").Filter(new() { HasText = SiblingRuleName }).WaitForAsync();
        }
        await Assertions.Expect(page.GetByLabel("规则名称", new() { Exact = true })).ToBeEnabledAsync();
    }

    // 沿原规则保存段编辑父子草稿与节点切换；分支裁决及事务语义由低层验证。
    private async Task AddRuleTreeAsync(string rootName, Func<Task>? captureDraftGuard)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "添加子规则", Exact = true }).ClickAsync();
        await page.GetByLabel("规则名称", new() { Exact = true }).FillAsync(ChildRuleName);
        await SelectAsync("直接属性", "ENAME");
        await SelectAsync("条件项 1 比较", "不为空");
        await page.GetByRole(AriaRole.Button, new() { Name = "添加主体项", Exact = true }).ClickAsync();
        var childSubject = page.GetByRole(AriaRole.Group, new() { Name = "主体授权项 1", Exact = true });
        await SelectAsync("读取意见", "同意", childSubject);

        await page.GetByRole(AriaRole.Button, new() { Name = "添加同级规则", Exact = true }).ClickAsync();
        await page.GetByLabel("规则名称", new() { Exact = true }).FillAsync(SiblingRuleName);
        await SelectAsync("直接属性", "ENAME");
        await SelectAsync("条件项 1 比较", "不为空");
        await page.Locator(".ant-tree-title").Filter(new() { HasText = ChildRuleName }).ClickAsync();
        await page.GetByText($"当前节点：{ChildRuleName}", new() { Exact = true }).WaitForAsync();
        await page.GetByText("完整条件还包含 1 个祖先条件", new() { Exact = true }).WaitForAsync();
        await page.GetByText($"当前根树：{rootName}", new() { Exact = true }).WaitForAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "新增规则", Exact = true }).ClickAsync();
        await page.GetByText("放弃未保存的草稿？", new() { Exact = true }).WaitForAsync();
        if (captureDraftGuard is not null) await captureDraftGuard();
        await page.GetByRole(AriaRole.Button, new() { Name = "继续编辑", Exact = true }).ClickAsync();
        await page.GetByText($"当前节点：{ChildRuleName}", new() { Exact = true }).WaitForAsync();
    }

    // 沿现有保存旅程操作嵌套条件，值比较与读取投影留给 SQL/API 测试。
    private async Task AddCompositeConditionAsync()
    {
        var root = page.GetByRole(AriaRole.Group, new() { Name = "条件项 1", Exact = true });
        await root.GetByRole(AriaRole.Button, new() { Name = "转换为 AND 组合", Exact = true }).ClickAsync();
        await root.GetByRole(AriaRole.Button, new() { Name = "添加 OR 组合", Exact = true }).ClickAsync();
        var branch = page.GetByRole(AriaRole.Group, new() { Name = "条件项 1.2", Exact = true });
        var leaf = page.GetByRole(AriaRole.Group, new() { Name = "条件项 1.2.1", Exact = true });
        await SelectAsync("条件项 1.2.1 直接属性", "ENAME", leaf);
        await SelectAsync("条件项 1.2.1 比较", "不为空", leaf);
        await branch.GetByRole(AriaRole.Button, new() { Name = "添加业务条件", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Group, new() { Name = "条件项 1.2.2", Exact = true })
            .GetByText("无读取贡献；完整判断仍生效", new() { Exact = true }).WaitForAsync();
    }

    // rc-select 的零高辅助节点也会包含选项；沿真实列表的键盘活动项选择，避免误点辅助节点。
    private async Task SelectAsync(string label, string option, ILocator? group = null, bool search = false,
        string? searchText = null, Func<Task>? candidatesReady = null)
    {
        var scope = group ?? page.Locator("body");
        var input = scope.GetByRole(AriaRole.Combobox, new() { Name = label, Exact = true });
        var selector = scope.Locator(".ant-select").Filter(new()
        {
            Has = page.GetByRole(AriaRole.Combobox, new() { Name = label, Exact = true })
        });
        await selector.Locator(".ant-select-selector").ClickAsync();
        var dropdown = page.Locator(".ant-select-dropdown:visible .rc-virtual-list-holder");
        if (search)
        {
            await input.FillAsync(searchText ?? option);
            await dropdown.Locator(".ant-select-item-option").Filter(new() { HasText = option })
                .WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
        if (candidatesReady is not null) await candidatesReady();
        var navigation = System.Diagnostics.Stopwatch.StartNew();
        var active = dropdown.Locator(".ant-select-item-option-active:not(.ant-select-item-option-disabled)")
            .Filter(new() { HasText = option });
        while (!await active.IsVisibleAsync())
        {
            if (navigation.Elapsed > TimeSpan.FromSeconds(10))
                throw new InvalidOperationException($"Select '{label}' has no reachable option '{option}'.");
            await input.PressAsync("ArrowDown");
        }
        await input.PressAsync("Enter");
        await Assertions.Expect(selector.Locator(".ant-select-selection-item")).ToContainTextAsync(option);
    }
}
