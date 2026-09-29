using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

internal sealed record DataScopeDepartmentSelection(string SearchText, string DisplayName);

/// <summary>规则管理的真实操作路径；过滤和权限数值由低层 API 测试证明。</summary>
internal sealed class DataScopeRulesPageDriver(IPage page)
{
    private const string ChildRuleName = "人员子例外";
    private const string SiblingRuleName = "人员旁支";

    /// <summary>沿当前表格与 Drawer 编辑角色主体；通过上移保持其高于默认主体的优先级。</summary>
    internal async Task CreatePersonRuleAsync(string name, string roleName,
        DataScopeDepartmentSelection? department = null, Func<Task>? captureDepartmentCandidates = null,
        bool includeCompositeCondition = false, bool includeRuleTree = false,
        Func<Task>? captureDraftGuard = null)
    {
        await page.Locator(".np-sidebar-menu").GetByRole(AriaRole.Link, new() { Name = "数据范围规则", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "＋ 新增规则", Exact = true }).ClickAsync();
        await page.GetByLabel("规则名称", new() { Exact = true }).FillAsync(name);
        await SelectAsync("资源", "人员中心");
        await EditAttributeConditionAsync("条件项 1", "绑定账号标识", currentAccount: true);
        if (includeCompositeCondition) await AddCompositeConditionAsync();
        await OpenSubjectsAsync();
        await SetReadOpinionAsync(SubjectRow(1), includeRuleTree ? "拒绝" : "同意");
        await AddSubjectAsync("角色", roleName);
        await SubjectRow(2).GetByRole(AriaRole.Button, new() { Name = "上移", Exact = true }).ClickAsync();
        if (department is not null)
            await AddSubjectAsync("部门", department.DisplayName, department.SearchText, captureDepartmentCandidates);
        if (includeRuleTree) await AddRuleTreeAsync(name, captureDraftGuard);
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "保存根规则", Exact = true }).ClickAsync(),
            response => response.Request.Method == "PUT" && response.Url.Contains("/api/authorization/data-scope/rules/", StringComparison.Ordinal));
        if (response.Status != 204) throw new InvalidOperationException($"Rule save returned {response.Status}.");
        await page.GetByText("规则已保存", new() { Exact = true }).WaitForAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "编辑规则名称", Exact = true })).ToBeEnabledAsync();
    }

    // 当前编辑器在 Drawer 中展示叶条件，资源与字段使用正式页面的中文显示名。
    private async Task EditAttributeConditionAsync(string rowName, string field, bool currentAccount = false)
    {
        await page.GetByRole(AriaRole.Row, new() { Name = rowName, Exact = true })
            .GetByRole(AriaRole.Button, new() { Name = "编辑", Exact = true }).ClickAsync();
        await SelectAsync("直接属性", field);
        if (currentAccount)
        {
            await SelectAsync("值来源", "当前身份值");
            await SelectAsync("当前身份值", "当前账号");
        }
        else await SelectAsync("条件项 1 比较", "不为空");
        await ConfirmDrawerAsync();
    }

    // 主体表格通过 Tab 展示；数量变化不改变该 Tab 的唯一身份。
    private Task OpenSubjectsAsync() => page.GetByRole(AriaRole.Tab,
        new() { NameRegex = new System.Text.RegularExpressions.Regex("^授权主体（[0-9]+）$") }).ClickAsync();

    // 排列后的行号由正式表格提供，不从主体 ID 或业务名称推断行结构。
    private ILocator SubjectRow(int index) => page.GetByRole(AriaRole.Row, new() { Name = $"主体授权项 {index}", Exact = true });

    // 意见控件只有三种状态；真实点击循环到目标，不替换 React 状态或直接写入载荷。
    private async Task SetReadOpinionAsync(ILocator scope, string opinion)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var target = scope.GetByRole(AriaRole.Checkbox, new() { Name = $"读取：{opinion}", Exact = true });
            if (await target.CountAsync() == 1) return;
            await scope.GetByRole(AriaRole.Checkbox, new()
                { NameRegex = new System.Text.RegularExpressions.Regex("^读取：(?:不选|同意|拒绝)$") }).ClickAsync();
        }
        await scope.GetByRole(AriaRole.Checkbox, new() { Name = $"读取：{opinion}", Exact = true }).WaitForAsync();
    }

    // 新主体只在 Drawer 确定后进入根草稿；候选回调仍在正式搜索弹层中执行。
    private async Task AddSubjectAsync(string type, string? name = null, string? searchText = null, Func<Task>? candidatesReady = null)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "添加主体项", Exact = true }).ClickAsync();
        await SelectAsync("主体类型", type);
        if (name is not null) await SelectAsync("主体选择", name, search: true, searchText: searchText, candidatesReady: candidatesReady);
        await SetReadOpinionAsync(page.Locator(".ant-drawer-open .ant-drawer-content"), "同意");
        await ConfirmDrawerAsync();
    }

    // Ant Design 两字按钮的正式可访问名含插入空格；关闭后再操作被遮罩的页面。
    private async Task ConfirmDrawerAsync()
    {
        var drawer = page.Locator(".ant-drawer-open .ant-drawer-content");
        await drawer.GetByRole(AriaRole.Button, new() { Name = "确 定", Exact = true }).ClickAsync();
        await drawer.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    /// <summary>重新装载已保存根，核对主体和条件行仍可识别并继续编辑。</summary>
    internal async Task ReloadRuleAsync(string name, string roleName, DataScopeDepartmentSelection department,
        bool expectCompositeCondition = false, bool expectRuleTree = false)
    {
        await page.ReloadAsync();
        await page.Locator(".ant-tree-title").Filter(new() { HasText = name }).ClickAsync();
        await OpenSubjectsAsync();
        await page.GetByRole(AriaRole.Row).Filter(new() { HasText = roleName }).WaitForAsync();
        await page.GetByRole(AriaRole.Row).Filter(new() { HasText = department.DisplayName }).WaitForAsync();
        if (expectCompositeCondition)
        {
            await page.GetByRole(AriaRole.Tab, new() { Name = "规则条件", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Row, new() { Name = "条件项 1.2.1", Exact = true }).WaitForAsync();
            var business = page.GetByRole(AriaRole.Row, new() { Name = "条件项 1.2.2", Exact = true });
            await business.GetByRole(AriaRole.Button, new() { Name = "编辑", Exact = true }).ClickAsync();
            await page.Locator(".ant-drawer-open").GetByText("无读取贡献；完整判断仍生效", new() { Exact = true }).WaitForAsync();
            await page.Locator(".ant-drawer-open").GetByRole(AriaRole.Button, new() { Name = "取 消", Exact = true }).ClickAsync();
            await page.Locator(".ant-drawer-open").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        }
        if (expectRuleTree)
        {
            await page.Locator(".ant-tree-title").Filter(new() { HasText = ChildRuleName }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "编辑规则名称", Exact = true })).ToHaveTextAsync(ChildRuleName);
            await page.GetByText("完整条件还包含 1 个祖先条件", new() { Exact = true }).WaitForAsync();
            await page.GetByText($"根规则：{name}", new() { Exact = true }).WaitForAsync();
            await page.Locator(".ant-tree-title").Filter(new() { HasText = SiblingRuleName }).WaitForAsync();
        }
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "编辑规则名称", Exact = true })).ToBeEnabledAsync();
    }

    // 节点操作由正式菜单持有；每个叶条件和主体仍经其 Drawer 确定后进入根草稿。
    private async Task AddRuleTreeAsync(string rootName, Func<Task>? captureDraftGuard)
    {
        await NodeActionAsync("添加子规则");
        await page.GetByLabel("规则名称", new() { Exact = true }).FillAsync(ChildRuleName);
        await EditAttributeConditionAsync("条件项 1", "姓名");
        await OpenSubjectsAsync();
        await AddSubjectAsync("所有用户");
        await NodeActionAsync("添加同级规则");
        await page.GetByLabel("规则名称", new() { Exact = true }).FillAsync(SiblingRuleName);
        await EditAttributeConditionAsync("条件项 1", "姓名");
        await page.Locator(".ant-tree-title").Filter(new() { HasText = ChildRuleName }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "编辑规则名称", Exact = true })).ToHaveTextAsync(ChildRuleName);
        await page.GetByText("完整条件还包含 1 个祖先条件", new() { Exact = true }).WaitForAsync();
        await page.GetByText($"根规则：{rootName}", new() { Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "＋ 新增规则", Exact = true }).ClickAsync();
        await page.GetByText("放弃未保存的草稿？", new() { Exact = true }).WaitForAsync();
        if (captureDraftGuard is not null) await captureDraftGuard();
        await page.GetByRole(AriaRole.Button, new() { Name = "继续编辑", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "编辑规则名称", Exact = true })).ToHaveTextAsync(ChildRuleName);
    }

    // 当前菜单在展开后提供唯一节点动作，不把菜单项误当页面按钮。
    private async Task NodeActionAsync(string name)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "节点操作 ▾", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = name, Exact = true }).ClickAsync();
    }

    // 两个同层条件由真实多选组合成 OR；整体 AND 与已有账号条件保持不变。
    private async Task AddCompositeConditionAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "新建条件", Exact = true }).ClickAsync();
        await SelectAsync("直接属性", "姓名");
        await SelectAsync("条件项 1 比较", "不为空");
        await ConfirmDrawerAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "新建条件", Exact = true }).ClickAsync();
        await SelectAsync("条件类型", "业务条件");
        await page.Locator(".ant-drawer-open").GetByText("无读取贡献；完整判断仍生效", new() { Exact = true }).WaitForAsync();
        await ConfirmDrawerAsync();
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "选择条件 2", Exact = true }).CheckAsync();
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "选择条件 3", Exact = true }).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "组合为任一满足", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Row, new() { Name = "条件项 1.2.1", Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Row, new() { Name = "条件项 1.2.2", Exact = true }).WaitForAsync();
    }

    // rc-select 的辅助列表零高，虚拟项可能已挂载却在裁剪区外；只点击当前弹层内实际命中的目标。
    internal async Task SelectAsync(string label, string option, ILocator? group = null, bool search = false,
        string? searchText = null, Func<Task>? candidatesReady = null)
    {
        var scope = group ?? page.Locator("body");
        var input = scope.GetByRole(AriaRole.Combobox, new() { Name = label, Exact = true });
        var selector = scope.Locator(".ant-select").Filter(new()
        {
            Has = page.GetByRole(AriaRole.Combobox, new() { Name = label, Exact = true })
        });
        await selector.Locator(".ant-select-selector").ClickAsync();
        var listId = await input.GetAttributeAsync("aria-controls")
            ?? throw new InvalidOperationException($"Select '{label}' has no popup identity.");
        var dropdown = page.Locator(".ant-select-dropdown")
            .Filter(new() { Has = page.Locator($"#{listId}") }).Locator(".rc-virtual-list-holder");
        await page.WaitForFunctionAsync("""
            id => {
              const popup = document.getElementById(id)?.closest('.ant-select-dropdown');
              const holder = popup?.querySelector('.rc-virtual-list-holder');
              const rect = holder?.getBoundingClientRect();
              const motion = [...(popup?.classList ?? [])].some(name =>
                /-(?:appear|enter|leave)(?:-(?:prepare|start|active))?$/.test(name));
              const expanded = document.querySelector(`[role="combobox"][aria-controls="${id}"]`)?.getAttribute('aria-expanded');
              return expanded === 'true' && !!rect && rect.width > 0 && rect.height > 0 && !motion
                && [...popup.getAnimations()].every(animation => animation.playState === 'finished');
            }
            """, listId);
        if (search)
        {
            await input.FillAsync(searchText ?? option);
            await dropdown.Locator(".ant-select-item-option").Filter(new() { HasText = option })
                .WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
        if (candidatesReady is not null) await candidatesReady();
        var navigation = System.Diagnostics.Stopwatch.StartNew();
        var hitPoint = await FindHitPointAsync();
        while (hitPoint is null)
        {
            if (navigation.Elapsed > TimeSpan.FromSeconds(10))
                throw new InvalidOperationException($"Select '{label}' has no reachable option '{option}'.");
            await input.PressAsync("ArrowDown");
            hitPoint = await FindHitPointAsync();
        }
        await page.Mouse.ClickAsync((float)hitPoint[0], (float)hitPoint[1]);
        await Assertions.Expect(selector.Locator(".ant-select-selection-item")).ToContainTextAsync(option);

        // 每次按当前布局重算裁剪交集和命中目标；不缓存虚拟项或过渡动画前的坐标。
        Task<double[]?> FindHitPointAsync() => page.EvaluateAsync<double[]?>("""
            ([id, text]) => {
              const holder = document.getElementById(id)?.closest('.ant-select-dropdown')?.querySelector('.rc-virtual-list-holder');
              const item = [...holder?.querySelectorAll('.ant-select-item-option:not(.ant-select-item-option-disabled)') ?? []]
                .find(value => value.textContent?.includes(text));
              if (!holder || !item) return null;
              const h = holder.getBoundingClientRect(), r = item.getBoundingClientRect();
              const x = (Math.max(h.left, r.left) + Math.min(h.right, r.right)) / 2;
              const y = (Math.max(h.top, r.top) + Math.min(h.bottom, r.bottom)) / 2;
              return x > h.left && x < h.right && y > h.top && y < h.bottom
                && item.contains(document.elementFromPoint(x, y)) ? [x, y] : null;
            }
            """, new[] { listId, option });
    }
}
