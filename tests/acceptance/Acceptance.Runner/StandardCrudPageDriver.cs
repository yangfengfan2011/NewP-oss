using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

// 一次写入交互期望观察到的 API 响应：先注册等待再点击，状态不符即抛异常。
internal sealed record ApiExpectation(
    string Method,
    string Path,
    int? ExpectedStatus,
    string Operation,
    bool PathIsPrefix = false)
{
    internal bool Matches(IResponse response)
    {
        if (!string.Equals(response.Request.Method, Method, StringComparison.Ordinal))
        {
            return false;
        }

        var actualPath = new Uri(response.Url).AbsolutePath;
        return PathIsPrefix
            ? actualPath.StartsWith(Path, StringComparison.Ordinal)
            : actualPath.Equals(Path, StringComparison.Ordinal);
    }
}

// 页面驱动：持有标准 CRUD 宿主控件的定位、等待与确认知识，不持有业务数据、旅程顺序或裁决。
// 一个实例绑定一个已登录的 IPage；切换 actor 就是换一个实例。
internal sealed class StandardCrudPageDriver
{
    internal const string OrganizationManagementTitle = "组织管理";
    internal const string SaveAndCloseLabel = "保存并关闭";
    internal const string SaveChangesLabel = "保存修改";
    private const string VisibleDrawerSelector = ".ant-drawer-content:visible";
    private const string VisibleModalSelector = ".ant-modal:visible";
    private const string VisibleOverlaySelector = ".ant-drawer-content:visible, .ant-modal:visible, [role=dialog]:visible";
    private const string TableRowSelector = ".ant-table-tbody tr.ant-table-row";
    // AntD 会在两字按钮文字之间注入空格，按可访问名称匹配时必须允许。
    private static readonly Regex ConfirmButtonName = new(@"^确\s*认$");

    internal StandardCrudPageDriver(IPage page)
    {
        Page = page ?? throw new ArgumentNullException(nameof(page));
    }

    internal IPage Page { get; }

    // ---- 标准列表入口 ----

    // 单一正式菜单与外层标签共同拥有入口，内嵌列表保留自己的筛选和 Drawer 状态。
    internal async Task OpenOrganizationTabAsync(string title, string key)
    {
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = OrganizationManagementTitle, Exact = true }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = title, Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/organization/structure"
            && (HasQueryValue(new Uri(url), "tab", key) || (key == "structure" && string.IsNullOrEmpty(new Uri(url).Query))));
    }

    // 按可见入口展开分组和溢出菜单；动作缺失仍返回局部诊断。
    internal async Task<ILocator> PreparePageActionAsync(string actionLabel, string? parentActionLabel = null)
    {
        var actionName = new Regex("^" + string.Join(@"\s*", actionLabel.Select(character => Regex.Escape(character.ToString()))) + "$");
        var button = Page.GetByRole(AriaRole.Button, new() { NameRegex = actionName });
        var menuAction = Page.GetByRole(AriaRole.Menuitem, new() { NameRegex = actionName });
        try
        {
            if (await menuAction.IsVisibleAsync()) return menuAction;
            if (parentActionLabel is null && await button.IsVisibleAsync()) return button;
            var triggerLabel = parentActionLabel ?? actionLabel;
            var trigger = Page.GetByRole(AriaRole.Button, new() { Name = triggerLabel, Exact = true });
            var more = Page.Locator(".np-action-toolbar-more:not([data-overflowed]) button");
            if (!await trigger.IsVisibleAsync() && await more.IsVisibleAsync())
            {
                await more.ClickAsync();
                trigger = Page.GetByRole(AriaRole.Menuitem, new() { Name = triggerLabel, Exact = true });
            }
            if (parentActionLabel is not null)
                await trigger.ClickAsync();
            var action = parentActionLabel is null && await button.IsVisibleAsync() ? button : menuAction;
            await action.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            return action;
        }
        catch (TimeoutException exception)
        {
            var diagnostic = await ReadPageDiagnosticAsync();
            throw new InvalidOperationException(
                $"Standard CRUD page did not expose '{actionLabel}'. {diagnostic}",
                exception);
        }
    }

    // 准备入口可供状态等待复用；只有本方法执行最终动作。
    internal async Task ClickPageActionAsync(string actionLabel, string? parentActionLabel = null) =>
        await (await PreparePageActionAsync(actionLabel, parentActionLabel)).ClickAsync();

    // ---- Drawer ----

    internal ILocator VisibleDrawer() => Page.Locator(VisibleDrawerSelector).Last;

    // 点击页级创建动作并等待最新的可见 Drawer 出现。
    internal async Task<ILocator> OpenCreateDrawerAsync(string actionLabel, string? parentActionLabel = null)
    {
        await ClickPageActionAsync(actionLabel, parentActionLabel);
        var drawer = VisibleDrawer();
        await WaitForDrawerAsync(drawer, WaitForSelectorState.Visible, $"drawer for '{actionLabel}'");
        return drawer;
    }

    internal async Task<ILocator> WaitForVisibleDrawerAsync(string description)
    {
        var drawer = VisibleDrawer();
        await WaitForDrawerAsync(drawer, WaitForSelectorState.Visible, description);
        return drawer;
    }

    internal static Task FillDrawerFieldAsync(ILocator drawer, string label, string value) =>
        drawer.GetByLabel(label, new LocatorGetByLabelOptions { Exact = true }).FillAsync(value);

    // 根据控件的可访问状态设置Switch输入，不对保存后的业务语义作浏览器断言。
    internal static async Task SetDrawerSwitchAsync(ILocator drawer, string label, bool enabled)
    {
        var control = drawer.GetByRole(AriaRole.Switch, new LocatorGetByRoleOptions { Name = label, Exact = true });
        await control.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        if (await control.GetAttributeAsync("aria-checked") != (enabled ? "true" : "false"))
            await control.ClickAsync();
    }

    internal Task<IResponse> SaveAndCloseAsync(ILocator drawer, ApiExpectation expectation) =>
        SubmitDrawerAsync(drawer, SaveAndCloseLabel, expectation);

    internal Task<IResponse> SaveChangesAsync(ILocator drawer, ApiExpectation expectation) =>
        SubmitDrawerAsync(drawer, SaveChangesLabel, expectation);

    // 不等待 API 响应的提交：只点击并等待 Drawer 关闭，供不观察写入响应的旅程使用。
    internal async Task SaveAndCloseWithoutResponseAsync(ILocator drawer)
    {
        await drawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = SaveAndCloseLabel, Exact = true }).ClickAsync();
        await WaitForDrawerAsync(drawer, WaitForSelectorState.Hidden, $"drawer after '{SaveAndCloseLabel}'");
    }

    // 先注册响应等待再点击，校验状态后等待 Drawer 关闭；返回响应供调用方读取业务字段。
    internal async Task<IResponse> SubmitDrawerAsync(ILocator drawer, string buttonLabel, ApiExpectation expectation)
    {
        var responseTask = Page.WaitForResponseAsync(expectation.Matches);
        await drawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = buttonLabel, Exact = true }).ClickAsync();
        var response = await responseTask;
        await EnsureExpectedStatusAsync(response, expectation);
        await WaitForDrawerAsync(drawer, WaitForSelectorState.Hidden, $"drawer after '{buttonLabel}' ({expectation.Operation})");
        return response;
    }

    internal async Task CloseDrawerAsync(ILocator drawer)
    {
        await drawer.Locator(".ant-drawer-close").ClickAsync();
        await WaitForDrawerAsync(drawer, WaitForSelectorState.Hidden, "drawer after close");
    }

    // ---- 共享确认弹窗 ----

    internal ILocator VisibleModal() => Page.Locator(VisibleModalSelector).Last;

    internal ILocator ConfirmDialogWithText(string text) =>
        Page.GetByRole(AriaRole.Dialog).Filter(new LocatorFilterOptions { HasTextString = text });

    // 在弹窗内点击确认，等待期望响应并校验，再等待弹窗关闭。
    internal async Task<IResponse> ConfirmAsync(ILocator dialog, ApiExpectation expectation, string? buttonLabel = null)
    {
        var responseTask = Page.WaitForResponseAsync(expectation.Matches);
        await (buttonLabel is null ? dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { NameRegex = ConfirmButtonName })
            : dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = buttonLabel, Exact = true })).ClickAsync();
        var response = await responseTask;
        await EnsureExpectedStatusAsync(response, expectation);
        await WaitForDrawerAsync(dialog, WaitForSelectorState.Hidden, $"dialog after confirm ({expectation.Operation})");
        return response;
    }

    // ---- 列表行 ----

    // 按顺序用多个文本收窄可见行，取最后一个匹配（新建行通常出现在末尾）。
    internal ILocator VisibleRow(string firstText, params string[] moreTexts)
    {
        var row = Page.Locator("tr:visible", new PageLocatorOptions { HasTextString = firstText });
        foreach (var text in moreTexts)
        {
            row = row.Filter(new LocatorFilterOptions { HasTextString = text });
        }

        return row.Last;
    }

    internal async Task<ILocator> WaitForRowAsync(string firstText, params string[] moreTexts)
    {
        var row = VisibleRow(firstText, moreTexts);
        await WaitForDrawerAsync(row, WaitForSelectorState.Visible, $"row containing '{firstText}'");
        return row;
    }

    internal ILocator TableRow(string text) =>
        Page.Locator(TableRowSelector).Filter(new LocatorFilterOptions { HasTextString = text }).Last;

    // 列表就绪以首行渲染为准。
    internal Task WaitForTableReadyAsync() =>
        WaitForDrawerAsync(Page.Locator(TableRowSelector).First, WaitForSelectorState.Visible, "first table row");

    // ---- 诊断 ----

    internal async Task<string> ReadPageDiagnosticAsync()
    {
        var capabilityError = Page.Locator("[data-route-capability-error]").First;
        if (await capabilityError.IsVisibleAsync())
        {
            var code = await capabilityError.GetAttributeAsync("data-route-capability-error");
            return $"Route capability error: {code ?? "unknown"}.";
        }

        var localError = Page.Locator(".sh-error-state:visible").First;
        if (await localError.IsVisibleAsync())
        {
            return $"Page error: {NormalizeDiagnostic(await localError.InnerTextAsync())}.";
        }

        var loading = Page.GetByRole(AriaRole.Status).First;
        if (await loading.IsVisibleAsync())
        {
            return $"Page status: {NormalizeDiagnostic(await loading.InnerTextAsync())}.";
        }

        var body = NormalizeDiagnostic(await Page.Locator("body").InnerTextAsync());
        return $"URL: {Page.Url}; no route capability, page error, or loading state was visible. Visible body: {body}.";
    }

    // 浏览器请求查询值按 application/x-www-form-urlencoded 解码后做精确比较，避免空格编码差异伪造搜索证据。
    internal static bool HasQueryValue(Uri uri, string key, string expectedValue)
    {
        foreach (var segment in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2
                && string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.Ordinal)
                && string.Equals(
                    Uri.UnescapeDataString(pair[1].Replace('+', ' ')),
                    expectedValue,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static string NormalizeDiagnostic(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private static async Task EnsureExpectedStatusAsync(IResponse response, ApiExpectation expectation)
    {
        var accepted = expectation.ExpectedStatus is int expected
            ? response.Status == expected
            : response.Ok;
        if (accepted)
        {
            return;
        }

        var body = await response.TextAsync();
        var expectedText = expectation.ExpectedStatus is int status ? status.ToString() : "2xx";
        throw new InvalidOperationException(
            $"Browser {expectation.Operation} returned {response.Status}, expected {expectedText}: {body}");
    }

    // 等待失败时把当前可见弹层文本附进异常，失败点自己说明“在等什么、看到什么”。
    private async Task WaitForDrawerAsync(ILocator target, WaitForSelectorState state, string description)
    {
        try
        {
            await target.WaitForAsync(new LocatorWaitForOptions { State = state });
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                $"Timed out waiting for {description} to become {state}. {await DescribeVisibleOverlaysAsync()}",
                exception);
        }
    }

    private async Task<string> DescribeVisibleOverlaysAsync()
    {
        try
        {
            var overlays = await Page.Locator(VisibleOverlaySelector).AllAsync();
            if (overlays.Count == 0)
            {
                return $"URL: {Page.Url}; no visible overlay.";
            }

            var texts = new List<string>(overlays.Count);
            foreach (var overlay in overlays)
            {
                texts.Add(NormalizeDiagnostic(await overlay.InnerTextAsync()));
            }

            return $"URL: {Page.Url}; visible overlays: {string.Join(" | ", texts)}";
        }
        catch (PlaywrightException failure)
        {
            return $"URL: {Page.Url}; overlay diagnostic unavailable: {failure.Message}";
        }
    }
}
