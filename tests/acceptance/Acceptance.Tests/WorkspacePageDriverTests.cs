using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Microsoft.Playwright;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class WorkspacePageDriverTests
{
    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "两种文件夹复制 driver 等待异步准备并消费各自正式提交端点，无服务器或端口")]
    public async Task FolderCopies_UseTheirOwnActionAndEndpointAfterPreparation()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        var paths = new List<string>();
        await page.RouteAsync("http://workspace.test/**", async route =>
        {
            if (route.Request.Method == "POST")
            {
                paths.Add(new Uri(route.Request.Url).AbsolutePath);
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
            }
            else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><button data-kind="copy">粘贴文件夹副本</button><button data-kind="copy-structure">粘贴文件夹结构</button>
                <table><tbody class="ant-table-tbody"></tbody></table>
                <script>
                  document.querySelectorAll('[data-kind]').forEach(button=>button.onclick=async()=>{
                    const dialog=document.createElement('div'); dialog.role='dialog'; dialog.setAttribute('aria-label',button.textContent);
                    dialog.innerHTML='<label>新文件夹名称<input disabled></label><button>确 认</button>'; document.body.append(dialog);
                    const input=dialog.querySelector('input');
                    dialog.querySelector('button').onclick=async()=>{
                      await fetch('/api/workspace/content/folders/'+button.dataset.kind,{method:'POST'});
                      const row=document.createElement('tr'); row.className='ant-table-row';
                      const cell=document.createElement('td'); cell.textContent=input.value; row.append(cell);
                      document.querySelector('tbody').append(row); dialog.remove();
                    };
                    await fetch('/api/workspace/content/folders/'+button.dataset.kind+'/prepare',{method:'POST'});
                    input.disabled=false;
                  });
                </script>
                """ });
        });
        await page.GotoAsync("http://workspace.test/");
        var driver = new WorkspacePageDriver(page);
        await driver.PasteFolderCopyAsync("浅副本");
        await driver.PasteFolderStructureAsync("结构副本");
        Assert.Equal(new[] { "/api/workspace/content/folders/copy/prepare", "/api/workspace/content/folders/copy",
            "/api/workspace/content/folders/copy-structure/prepare", "/api/workspace/content/folders/copy-structure" }, paths);
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "正式树动作driver按rc-tree真实选中class区分同名出现，无服务器或端口")]
    public async Task SelectedFolderAction_UsesTheSelectedOccurrenceWithoutAriaSelected()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(1_000);
        await page.RouteAsync("http://workspace.test/**", async route =>
        {
            if (route.Request.Method == "POST")
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
            else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><aside aria-label="结构树"><div role="tree">
                  <div role="treeitem" aria-label="共享目录" id="first"><span>共享目录</span></div>
                  <div role="treeitem" aria-label="共享目录" id="selected" class="ant-tree-treenode-selected"><span>共享目录</span></div>
                </div></aside>
                <section aria-label="节点内容"><h2>共享目录</h2><table class="ant-table"><tr><td>内容</td></tr></table></section>
                <div role="menu" hidden><button role="menuitem">复制</button></div>
                <script>
                  document.querySelectorAll('[role=treeitem] span').forEach(title=>title.oncontextmenu=event=>{
                    event.preventDefault(); window.contextTarget=title.parentElement.id;
                    document.querySelector('[role=menu]').hidden=false;
                  });
                  document.querySelector('[role=menuitem]').onclick=async()=>{
                    await fetch('/api/workspace/content/clipboard/validate',{method:'POST'});
                    document.querySelector('[role=menu]').hidden=true;
                  };
                </script>
                """ });
        });
        await page.GotoAsync("http://workspace.test/");
        await new WorkspacePageDriver(page).CopyFolderFromTreeContextMenuAsync("共享目录", selectedOnly: true);
        Assert.Equal("selected", await page.EvaluateAsync<string>("window.contextTarget"));
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "批量删除 driver 兼容正式工具栏与确认按钮的 AntD 双汉字空格，无服务器或端口")]
    public async Task BatchDelete_UsesSharedSpacedConfirmationButton()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        foreach (var actionLabel in new[] { "删除", "删 除" })
        {
            var page = await browser.NewPageAsync();
            page.SetDefaultTimeout(1_000);
            await page.RouteAsync("http://workspace.test/**", async route =>
            {
                if (route.Request.Method == "POST")
                    await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" });
                else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                    <meta charset="utf-8"><section aria-label="节点内容">
                    <button id="batch">ACTION_LABEL</button><table><tbody class="ant-table-tbody">
                    <tr class="ant-table-row"><td><input type="checkbox"></td><td>甲对象</td></tr>
                    <tr class="ant-table-row"><td><input type="checkbox"></td><td>乙对象</td></tr>
                    </tbody></table></section>
                    <div role="dialog" aria-label="删除" hidden><button id="confirm">确 认</button></div>
                    <script>
                    document.querySelector('#batch').onclick=()=>{
                      if(document.querySelectorAll('input:checked').length===2) document.querySelector('[role=dialog]').hidden=false;
                    };
                    document.querySelector('#confirm').onclick=async()=>{
                      await fetch('/api/workspace/content/delete',{method:'POST'});
                      document.querySelectorAll('tr,[role=dialog]').forEach(element=>element.remove());
                    };
                    </script>
                    """.Replace("ACTION_LABEL", actionLabel) });
            });
            await page.GotoAsync("http://workspace.test/");
            await new WorkspacePageDriver(page).DeleteSelectedAsync("甲对象", "乙对象");
            await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
            await page.CloseAsync();
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "批量移除 driver 兼容正式工具栏的 AntD 双汉字空格并保留确认与写入等待，无服务器或端口")]
    public async Task BatchRemove_UsesSpacedToolbarButton()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        foreach (var actionLabel in new[] { "移除", "移 除" })
        {
            var page = await browser.NewPageAsync();
            page.SetDefaultTimeout(1_000);
            await page.RouteAsync("http://workspace.test/**", async route =>
            {
                if (route.Request.Method == "POST")
                    await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" });
                else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                    <meta charset="utf-8"><section aria-label="节点内容">
                    <button id="batch">ACTION_LABEL</button><table><tbody class="ant-table-tbody">
                    <tr class="ant-table-row"><td><input type="checkbox"></td><td>甲对象</td></tr>
                    <tr class="ant-table-row"><td><input type="checkbox"></td><td>乙对象</td></tr>
                    </tbody></table></section>
                    <div role="dialog" hidden>仅从当前文件夹移除所选引用，保留对象及文件，确定继续？<button id="confirm">确 认</button></div>
                    <script>
                    document.querySelector('#batch').onclick=()=>{
                      if(document.querySelectorAll('input:checked').length===2) document.querySelector('[role=dialog]').hidden=false;
                    };
                    document.querySelector('#confirm').onclick=async()=>{
                      await fetch('/api/workspace/content/references/remove',{method:'POST'});
                      document.querySelectorAll('tr,[role=dialog]').forEach(element=>element.remove());
                    };
                    </script>
                    """.Replace("ACTION_LABEL", actionLabel) });
            });
            await page.GotoAsync("http://workspace.test/");
            await new WorkspacePageDriver(page).RemoveSelectedReferencesAsync(["甲对象", "乙对象"]);
            await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
            await page.CloseAsync();
        }
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

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实 Chromium 验证附件多选及同名下载按钮按文件行限定，无服务器或端口")]
    public async Task DocumentCollection_UsesVisibleMultipleChooserAndDownloadsEachFileRow()
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
            if (new Uri(route.Request.Url).AbsolutePath.EndsWith("/content", StringComparison.Ordinal))
            {
                await route.FulfillAsync(new() { Status = 200, ContentType = "application/octet-stream", Body = "driver fixture",
                    Headers = new Dictionary<string, string> { ["Content-Disposition"] = "attachment; filename=driver-download.txt" } });
                return;
            }
            await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><div class="ant-drawer-content" role="dialog" aria-label="新建图文档">
                  <div class="ant-form-item"><label for="ECODE">编码</label><input id="ECODE"></div>
                  <div class="ant-form-item"><label for="ENAME">名称</label><input id="ENAME"></div>
                  <label for="main">主文件</label><input id="main" type="file" hidden>
                  <button id="choose-main">选择主文件</button>
                  <label for="attachments">附件</label><input id="attachments" type="file" multiple hidden>
                  <button id="choose-attachments">选择附件</button><div id="attachment-rows"></div>
                  <button id="save" disabled>保存并关闭</button>
                </div>
                <section aria-label="文件"><table><tbody id="downloads"></tbody></table></section>
                <script>
                  window.attachmentChooseCount=0;
                  const addDownload = name => {
                    const row=document.createElement('tr');
                    const nameCell=document.createElement('td'); nameCell.textContent=name;
                    const actionCell=document.createElement('td');
                    const button=document.createElement('button'); button.textContent='下载';
                    button.onclick=()=>{ const link=document.createElement('a'); link.href='/files/'+encodeURIComponent(name)+'/content'; link.click(); };
                    actionCell.append(button); row.append(nameCell,actionCell); document.querySelector('#downloads').append(row);
                  };
                  document.querySelector('#choose-main').onclick=()=>document.querySelector('#main').click();
                  document.querySelector('#choose-attachments').onclick=()=>{ window.attachmentChooseCount++; document.querySelector('#attachments').click(); };
                  document.querySelector('#main').onchange=async event=>{
                    await fetch('/api/workspace/content/document-uploads',{method:'POST'});
                    addDownload(event.target.files[0].name);
                  };
                  document.querySelector('#attachments').onchange=async event=>{
                    for(const file of event.target.files) {
                      const row=document.createElement('div'); row.role='group'; row.setAttribute('aria-label','附件 '+file.name);
                      document.querySelector('#attachment-rows').append(row);
                      await fetch('/api/workspace/content/document-uploads',{method:'POST'});
                      row.textContent='附件已上传，保存后生效。'; addDownload(file.name);
                    }
                    document.querySelector('#save').disabled=false;
                  };
                </script>
                """ });
        });
        await page.GotoAsync("http://workspace.test/");
        var attachments = WorkspacePageDriver.SmallAttachmentFiles();
        var driver = new WorkspacePageDriver(page);
        await driver.FillDocumentCreationAsync(page.GetByRole(AriaRole.Dialog), "D-2", "集合图文档", WorkspacePageDriver.SmallMainFile(), attachments);
        Assert.Equal(3, stages);
        Assert.Equal(1, await page.EvaluateAsync<int>("window.attachmentChooseCount"));
        await Assertions.Expect(page.GetByLabel("附件", new() { Exact = true })).ToBeHiddenAsync();
        var output = Path.Combine(Path.GetTempPath(), "NewPDriverCollection_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            foreach (var file in new[] { WorkspacePageDriver.SmallMainFile() }.Concat(attachments))
            {
                var path = Path.Combine(output, file.Name);
                await driver.DownloadFileAsync(file.Name, path);
                Assert.True(File.Exists(path));
            }
        }
        finally { Directory.Delete(output, recursive: true); }
    }
    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实 Chromium 复现 AntD 选中项遮挡内部输入，语言选择使用可点击外框")]
    public async Task LanguageSelection_ClicksSelectSurfaceWithOverlaidReadOnlyInput()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(1_000);
        await page.SetContentAsync("""
            <style>
              .ant-select-selector {position:relative;width:120px;height:32px;border:1px solid #999;}
              input {position:absolute;inset:0;width:100%;height:100%;box-sizing:border-box;}
              .ant-select-selection-item {position:absolute;inset:0;display:flex;align-items:center;background:white;}
            </style>
            <header><div class="np-shell-language-select"><div class="ant-select-selector">
              <input role="combobox" aria-label="语言" readonly>
              <span class="ant-select-selection-item">简体中文</span>
            </div></div></header>
            <div class="ant-select-dropdown" hidden><div>English</div><div>简体中文</div></div>
            <script>
              const menu=document.querySelector('.ant-select-dropdown');
              const selected=document.querySelector('.ant-select-selection-item');
              document.querySelector('.ant-select-selector').onclick=()=>menu.hidden=false;
              menu.querySelectorAll('div').forEach(option=>option.onclick=()=>{ selected.textContent=option.textContent; menu.hidden=true; });
            </script>
            """);
        var driver = new WorkspacePageDriver(page);
        await driver.SelectLanguageAsync("English");
        await Assertions.Expect(page.Locator(".ant-select-selection-item")).ToHaveTextAsync("English");
        await driver.SelectLanguageAsync("简体中文");
        await Assertions.Expect(page.Locator(".ant-select-selection-item")).ToHaveTextAsync("简体中文");
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
