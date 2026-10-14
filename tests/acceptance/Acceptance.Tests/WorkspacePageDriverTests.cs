using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Microsoft.Playwright;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class WorkspacePageDriverTests
{
    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "批量删除 driver 复用共享确认按钮的 AntD 空格兼容定位，无服务器或端口")]
    public async Task BatchDelete_UsesSharedSpacedConfirmationButton()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        await page.RouteAsync("http://workspace.test/**", async route =>
        {
            if (route.Request.Method == "POST")
                await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" });
            else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><section aria-label="节点内容">
                <button id="batch">批量删除</button><table><tbody class="ant-table-tbody">
                <tr class="ant-table-row"><td><input type="checkbox"></td><td>甲对象</td></tr>
                <tr class="ant-table-row"><td><input type="checkbox"></td><td>乙对象</td></tr>
                </tbody></table></section>
                <div role="dialog" aria-label="批量删除" hidden><button id="confirm">确 认</button></div>
                <script>
                document.querySelector('#batch').onclick=()=>{
                  if(document.querySelectorAll('input:checked').length===2) document.querySelector('[role=dialog]').hidden=false;
                };
                document.querySelector('#confirm').onclick=async()=>{
                  await fetch('/api/workspace/content/delete',{method:'POST'});
                  document.querySelectorAll('tr,[role=dialog]').forEach(element=>element.remove());
                };
                </script>
                """ });
        });
        await page.GotoAsync("http://workspace.test/");
        await new WorkspacePageDriver(page).DeleteSelectedAsync("甲对象", "乙对象");
        await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "Chromium中反向Modal及双Drawer按对象限定关闭，无服务器或端口")]
    public async Task ReferencingFolderDrawer_DoesNotSelectOrCloseTheUnderlyingObjectDrawer()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        // 保留生产门户的并存结构；姓名只能在目标Drawer内匹配，不能依赖门户顺序。
        await page.SetContentAsync("""
            <main>公共空间</main>
            <div class="ant-drawer-content" id="source"><button class="ant-drawer-close">关闭</button><p>源文件夹</p><p>基本信息</p><p>审计信息</p></div>
            <div class="ant-modal" role="dialog" aria-label="直接引用文件夹"><button class="ant-modal-close">关闭</button></div>
            <div class="ant-drawer-content" id="parent"><button class="ant-drawer-close">关闭</button><p>公共空间</p><p>基本信息</p><p>审计信息</p></div>
            <script>
              document.querySelectorAll('.ant-drawer-close,.ant-modal-close').forEach(button=>button.onclick=()=>button.parentNode.remove());
            </script>
            """);
        var driver = new WorkspacePageDriver(page);
        var parent = await driver.WaitForFolderDrawerAsync("公共空间");
        await driver.ClosePropertiesAsync(parent);
        await Assertions.Expect(page.Locator("#source")).ToBeVisibleAsync();
        await driver.CloseDirectReferencesAsync(page.GetByRole(AriaRole.Dialog, new() { Name = "直接引用文件夹" }));
        await Assertions.Expect(page.Locator("#source")).ToBeVisibleAsync();
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "Chromium中共享树driver只消费节点内容面包屑，无服务器或端口")]
    public async Task NodeBreadcrumb_IgnoresTheShellNavigationWithTheSameAccessibleName()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync("""
            <nav aria-label="面包屑"><button>路径A</button><span>Shell</span></nav>
            <section aria-label="节点内容"><header class="np-tree-context-header">
              <nav aria-label="面包屑"><button>公共空间</button><button id="ancestor">路径A</button><span>后代T</span></nav>
              <h2>后代T</h2></header></section>
            <script>document.querySelector('#ancestor').onclick=()=>document.querySelector('h2').textContent='路径A';</script>
            """);
        var driver = new StandardTreePageDriver(page);
        await driver.WaitForBreadcrumbAsync("公共空间", "路径A", "后代T");
        await driver.ClickAncestorAsync("路径A");
        await Assertions.Expect(page.GetByRole(AriaRole.Region).GetByRole(AriaRole.Heading)).ToHaveTextAsync("路径A");
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实 Chromium 的 ARIA 失败快照沿既有登记凭据脱敏，无服务器或端口")]
    public async Task AriaDiagnostic_RedactsRegisteredInputValueAndPreservesTheControlName()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        const string credential = "registered-secret-value";
        await page.SetContentAsync($"<label for='candidate'>对象</label><input id='candidate' role='combobox' value='{credential}'>");
        var snapshot = await WorkspaceAcceptanceRunner.ReadSanitizedAriaAsync(page.Locator("body"), value => AcceptanceInfrastructure.Sanitize([credential], value));
        Assert.Contains("对象", snapshot);
        Assert.Contains("[REDACTED]", snapshot);
        Assert.DoesNotContain(credential, snapshot);
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实 Chromium 验证正式新建表单 driver 的必填字段填写，无服务器或端口")]
    public async Task ObjectCreation_FillsRequiredFieldsBeforeConfirming()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync("""
            <div class="ant-drawer-content" role="dialog" aria-label="新建零组件">
              <div class="ant-form-item"><label for="ECODE">编码</label><input id="ECODE"></div>
              <div class="ant-form-item"><label for="ENAME">名称</label><input id="ENAME"></div>
              <button disabled>保存并关闭</button>
            </div>
            <script>
              document.querySelectorAll('input').forEach(input => input.oninput = () => {
                document.querySelector('button').disabled = !document.querySelector('#ECODE').value || !document.querySelector('#ENAME').value;
              });
            </script>
            """);
        await new WorkspacePageDriver(page).FillObjectCreationAsync(page.GetByRole(AriaRole.Dialog), "P-1", "新建对象");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "保存并关闭" })).ToBeEnabledAsync();
    }
    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实 Chromium 验证正式单main driver 在暂存完成后才等待保存，无服务器或端口")]
    public async Task DocumentCreation_StagesMainBeforeWaitingForSave()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        var stages = 0;
        await page.RouteAsync("http://workspace.test/**", async route =>
        {
            if (route.Request.Method == "POST")
            {
                stages++;
                await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" });
                return;
            }
            await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><div class="ant-drawer-content" role="dialog" aria-label="新建图文档">
                  <div class="ant-form-item"><label for="ECODE">编码</label><input id="ECODE"></div>
                  <div class="ant-form-item"><label for="ENAME">名称</label><input id="ENAME"></div>
                  <label for="main">主文件</label><input id="main" type="file" hidden>
                  <button id="choose">选择主文件</button><button id="save" disabled>保存并关闭</button>
                </div>
                <script>
                  window.chooseCount=0;
                  document.querySelector('#choose').onclick=()=>{ window.chooseCount++; document.querySelector('#main').click(); };
                  document.querySelector('#main').onchange=async()=>{
                    if(!document.querySelector('#ECODE').value || !document.querySelector('#ENAME').value) throw new Error('Fields must precede Stage');
                    await fetch('/api/workspace/content/document-uploads',{method:'POST'});
                    document.querySelector('#save').disabled=false;
                  };
                </script>
                """ });
        });
        await page.GotoAsync("http://workspace.test/");
        await new WorkspacePageDriver(page).FillDocumentCreationAsync(page.GetByRole(AriaRole.Dialog), "D-1", "带主文件图文档", WorkspacePageDriver.SmallMainFile());
        Assert.Equal(1, stages);
        Assert.Equal(1, await page.EvaluateAsync<int>("window.chooseCount"));
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "选择主文件", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("主文件", new() { Exact = true })).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "保存并关闭" })).ToBeEnabledAsync();
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实 Chromium 验证正式 driver 的多加载指示器等待，无服务器或端口")]
    public async Task SelectedRoot_WaitsUntilBothVisibleLoadingIndicatorsHaveFinished()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        // 保留失败现场的双层 loading 结构，两个组件可以分别完成；driver 不能只等第一项。
        await page.SetContentAsync("""
            <aside aria-label="结构树">公共空间</aside>
            <section aria-label="节点内容"><h2>公共空间</h2><table class="ant-table"><tr><td>列表</td></tr></table>
              <div id="outer" class="ant-spin-spinning">列表容器加载</div>
              <div id="inner" class="ant-spin-spinning">正在加载列表</div>
            </section>
            """);
        var ready = new WorkspacePageDriver(page).SelectRootAsync("公共空间");
        // 留出浏览器 driver 进入等待的轮次，再分别完成两个独立加载者。
        await Task.Delay(100);
        if (ready.IsFaulted) await ready;
        Assert.False(ready.IsCompleted);
        await page.Locator("#outer").EvaluateAsync("element => element.remove()");
        await Task.Delay(100);
        Assert.False(ready.IsCompleted, "另一加载者仍可见时不能报告列表就绪。");
        await page.Locator("#inner").EvaluateAsync("element => element.remove()");
        await ready;
    }
}
