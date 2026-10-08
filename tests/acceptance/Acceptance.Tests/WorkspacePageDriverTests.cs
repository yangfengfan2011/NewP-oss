using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Microsoft.Playwright;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class WorkspacePageDriverTests
{
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

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实 Chromium 验证正式候选 driver 的必填标签及选择，无服务器或端口")]
    public async Task ObjectCandidate_SelectsTypeThenRequiredLookupWithoutConfusingTheirSelectedLabels()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(1000);
        // 正式标准表单有 enum 和 required Lookup 两个 Select，回显必须归属对象字段。
        await page.SetContentAsync("""
            <style>.ant-form-item-required::before { content: '*'; margin-right: 4px; }</style>
            <style>.ant-select-selector { position:relative; width:300px; height:32px; }
              .ant-select-selector input { width:100%; height:100%; }
              .ant-select-selection-item { position:absolute; inset:0; background:white; }</style>
            <div class="ant-modal" role="dialog" aria-label="添加已有对象">
              <div class="ant-form-item"><label for="type">对象类型</label><div class="ant-select-selector"><input id="type" role="combobox" readonly>
                <span class="ant-select-selection-item">文件夹</span></div></div>
              <div class="ant-form-item"><label for="folder" class="ant-form-item-required">对象</label>
                <div class="ant-select-selector"><input id="folder" role="combobox" aria-required="true"><span class="ant-select-selection-item"></span></div></div>
              <button disabled>确 认</button>
            </div>
            <div class="ant-select-dropdown" id="types" hidden><div>零组件</div></div>
            <div class="ant-select-dropdown" id="objects" hidden><div class="ant-select-item-option-content">P-1 - 来源对象</div></div>
            <script>
              document.querySelector('#type').parentNode.onclick=()=>document.querySelector('#types').hidden=false;
              document.querySelector('#types div').onclick=()=>{
                document.querySelector('#types').hidden=true;
                document.querySelector('#type').parentNode.querySelector('.ant-select-selection-item').textContent='零组件';
              };
              const input=document.querySelector('#folder');
              input.oninput=()=>document.querySelector('#objects').hidden=false;
              document.querySelector('.ant-select-item-option-content').onclick=()=>{
                input.parentNode.querySelector('.ant-select-selection-item').textContent='P-1 - 来源对象';
                document.querySelector('button').disabled=false;
                document.querySelector('#objects').hidden=true;
              };
            </script>
            """);
        await new WorkspacePageDriver(page).SelectObjectCandidateAsync(page.GetByRole(AriaRole.Dialog), "零组件", "来源对象", "P-1 - 来源对象");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "确 认" })).ToBeEnabledAsync();
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
