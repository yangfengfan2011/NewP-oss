using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Microsoft.Playwright;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class WorkspacePageDriverTests
{
    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实 Chromium 验证正式候选 driver 的必填标签及选择，无服务器或端口")]
    public async Task FolderCandidate_SelectsTheRequiredComboboxAndWaitsForReadyConfirmation()
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
        // 正式 HTML/ARIA 现场：必填星号属于 label 文本，combobox 名称为“* 文件夹”。
        await page.SetContentAsync("""
            <style>.ant-form-item-required::before { content: '*'; margin-right: 4px; }</style>
            <div class="ant-modal" role="dialog" aria-label="添加已有文件夹">
              <label for="folder" class="ant-form-item-required">文件夹</label>
              <input id="folder" role="combobox" aria-required="true">
              <span class="ant-select-selection-item"></span><button disabled>确 认</button>
            </div>
            <div class="ant-select-dropdown" hidden><div class="ant-select-item-option-content">来源文件夹</div></div>
            <script>
              const input=document.querySelector('input');
              input.oninput=()=>document.querySelector('.ant-select-dropdown').hidden=false;
              document.querySelector('.ant-select-item-option-content').onclick=()=>{
                document.querySelector('.ant-select-selection-item').textContent='来源文件夹';
                document.querySelector('button').disabled=false;
                document.querySelector('.ant-select-dropdown').hidden=true;
              };
            </script>
            """);
        await new WorkspacePageDriver(page).SelectFolderCandidateAsync(page.GetByRole(AriaRole.Dialog), "来源文件夹");
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
