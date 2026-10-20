using Microsoft.Playwright;

namespace ICP.Platform.Acceptance;

/// <summary>纯功能命令复用标准宿主真实控件；业务与权限语义留在对应API测试。</summary>
internal sealed class FunctionalCommandPageDriver(IPage page)
{
    private readonly StandardCrudPageDriver _crud = new(page);

    internal async Task RenameAccountAsync(string code, string name)
    {
        await page.Locator(".np-sidebar-menu").GetByRole(AriaRole.Link, new() { Name = "账号管理", Exact = true }).ClickAsync();
        var row = await _crud.WaitForRowAsync(code);
        await row.GetByRole(AriaRole.Button, new() { Name = "编辑", Exact = true }).ClickAsync();
        var drawer = await _crud.WaitForVisibleDrawerAsync("纯功能账号编辑");
        await StandardCrudPageDriver.FillDrawerFieldAsync(drawer, "显示名称", name);
        await _crud.SaveChangesAsync(drawer, new("PUT", "/api/identity/accounts/", 200, "账号改名", PathIsPrefix: true));
        await _crud.WaitForRowAsync(code, name);
    }

    internal async Task InitializeOrganizationAsync()
    {
        await _crud.OpenOrganizationTabAsync("公司", "companies");
        // 列表无输入的页动作直接提交；沿既有Ownership场景的实际入口先登记响应等待。
        var initialized = page.WaitForResponseAsync(response => response.Request.Method == "POST"
            && new Uri(response.Url).AbsolutePath == "/api/organization/initialization");
        await _crud.ClickPageActionAsync("初始化默认组织");
        var response = await initialized;
        if (response.Status != 200) throw new InvalidOperationException($"默认组织初始化返回 {response.Status}。");
        await _crud.WaitForTableReadyAsync();
    }

    internal async Task BindAndChangePersonStatusAsync(string personNo, string personName, string accountCode)
    {
        await _crud.OpenOrganizationTabAsync("人员", "persons");
        var row = await _crud.WaitForRowAsync(personNo);
        await row.GetByRole(AriaRole.Button, new() { Name = personName, Exact = true }).ClickAsync();
        var drawer = await _crud.WaitForVisibleDrawerAsync("纯功能人员详情");
        await drawer.GetByRole(AriaRole.Button, new() { Name = "查看完整详情", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "建立永久绑定", Exact = true }).ClickAsync();
        var dialog = _crud.VisibleModal();
        await dialog.GetByLabel("按 ECODE 或 ENAME 搜索", new() { Exact = true }).FillAsync(accountCode);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "搜索候选", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Radio, new() { Name = "选择 " + accountCode, Exact = true }).CheckAsync();
        await dialog.GetByLabel("绑定原因", new() { Exact = true }).FillAsync("正式按钮用户路径");
        await dialog.GetByRole(AriaRole.Checkbox).CheckAsync();
        await _crud.ConfirmAsync(dialog, new("PUT", "/api/organization/persons/", 200, "永久绑定", PathIsPrefix: true), "确认永久绑定");
        await page.GetByText("已永久绑定", new() { Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "停用", Exact = true }).ClickAsync();
        dialog = _crud.VisibleModal();
        await dialog.GetByLabel("停用原因", new() { Exact = true }).FillAsync("正式按钮停用路径");
        await _crud.ConfirmAsync(dialog, new("PUT", "/api/organization/persons/", 200, "人员停用", PathIsPrefix: true));
        await _crud.OpenOrganizationTabAsync("人员", "persons");
        row = await _crud.WaitForRowAsync(personNo);
        await row.GetByRole(AriaRole.Button, new() { Name = "更多", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "启用", Exact = true }).ClickAsync();
        await _crud.ConfirmAsync(_crud.VisibleModal(), new("PUT", "/api/organization/persons/", 200, "人员恢复", PathIsPrefix: true));
        await _crud.WaitForRowAsync(personNo, personName);
    }
}
