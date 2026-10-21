using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Microsoft.Playwright;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class WorkspacePageDriverTests
{
    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实Chromium按正式href点击可见侧栏链接，保留元数据键fallback，不直接导航，无Host或SQL")]
    public async Task StandardDataSidebar_ClicksItsFormalPathWhenTheAccessibleNameFallsBackToTheMetadataKey()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        const string path = "/plugins/partner-asset-catalog/assets";
        await page.RouteAsync("http://sidebar.test/**", async route => await route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = """
                <meta charset="utf-8"><nav class="np-sidebar-menu">
                  <a href="/wrong">资产</a><section aria-expanded="true">
                    <a href="/plugins/partner-asset-catalog/assets" onclick="sessionStorage.setItem('menuClicks', String(Number(sessionStorage.getItem('menuClicks') || 0) + 1))">partner-asset-catalog.asset</a>
                  </section>
                </nav>
                """
        }));
        await page.GotoAsync("http://sidebar.test/");
        await new StandardCrudPageDriver(page).OpenSidebarPathAsync(path);
        Assert.Equal(path, new Uri(page.Url).AbsolutePath);
        Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.getItem('menuClicks')"));
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实Chromium复现整组核对后Review仍dirty，取消必须经标准丢弃确认，无服务器或端口")]
    public async Task ReconciledBatchReview_CancelsThroughTheExistingDiscardConfirmationWithoutSaving()
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
            <meta charset="utf-8"><div class="ant-modal" role="dialog" aria-label="批量授权">
              <button>开始新的授权操作</button><div class="ant-modal-footer"><button id="cancel">取消</button><button id="save" disabled>确认</button></div>
            </div>
            <script>
              window.saveCount=0; window.discardCount=0;
              document.querySelector('#save').onclick=()=>window.saveCount++;
              document.querySelector('#cancel').onclick=()=>{
                const discard=document.createElement('div'); discard.className='ant-modal'; discard.role='dialog';
                discard.setAttribute('aria-label','放弃未保存的草稿？');
                discard.innerHTML='<h2>放弃未保存的草稿？</h2><button>继续编辑</button><button id="discard">放弃修改</button>';
                document.body.append(discard);
                discard.querySelector('#discard').onclick=()=>{window.discardCount++; document.querySelectorAll('.ant-modal').forEach(element=>element.remove());};
              };
            </script>
            """);
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "批量授权", Exact = true });
        await new StandardCrudPageDriver(page).CancelAndDiscardReviewAsync(dialog);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.saveCount"));
        Assert.Equal(1, await page.EvaluateAsync<int>("window.discardCount"));
        await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "真实Chromium核对共用对象授权driver只读关闭零写、管理仅一次保存，无Host或SQL")]
    public async Task ObjectAuthorization_ClosesInspectionWithoutWritingAndSavesManagementOnce()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        var requests = new List<string>();
        const string target = "/api/authorization/data-scope/objects/example.record/record";
        await page.RouteAsync("http://example.test/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.StartsWith(target, StringComparison.Ordinal))
            {
                requests.Add(route.Request.Method + " " + path);
                if (route.Request.Method == "PUT")
                    await route.FulfillAsync(new() { Status = 204 });
                else await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
                return;
            }
            await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><table><tr><td>Example record</td><td><button id="more">更多</button></td></tr></table>
                <div role="menu" hidden><button role="menuitem" id="inspect">查看授权</button><button role="menuitem" id="manage">授权</button></div>
                <script>
                  const target='/api/authorization/data-scope/objects/example.record/record';
                  const menu=document.querySelector('[role=menu]'); window.inspectionClosed=0;
                  document.querySelector('#more').onclick=()=>menu.hidden=false;
                  document.querySelector('#inspect').onclick=async()=>{
                    menu.hidden=true; await fetch(target+'/inspection/explanation');
                    const dialog=document.createElement('div'); dialog.className='ant-modal'; dialog.role='dialog';
                    dialog.innerHTML='<button>关闭</button>'; document.body.append(dialog);
                    dialog.querySelector('button').onclick=()=>{window.inspectionClosed++; dialog.remove();};
                  };
                  document.querySelector('#manage').onclick=async()=>{
                    menu.hidden=true; await fetch(target);
                    const dialog=document.createElement('div'); dialog.className='ant-modal'; dialog.role='dialog';
                    dialog.innerHTML='<button id="add">添加主体项</button><label><input type="checkbox">读取：不选</label><button id="save">保存授权</button>';
                    document.body.append(dialog);
                    dialog.querySelector('#add').onclick=()=>{
                      const editor=document.createElement('section'); editor.setAttribute('data-subject-editor','');
                      editor.innerHTML='<button>确定</button>'; dialog.append(editor);
                      editor.querySelector('button').onclick=()=>editor.remove();
                    };
                    dialog.querySelector('#save').onclick=async()=>{await fetch(target,{method:'PUT'}); dialog.remove();};
                  };
                </script>
                """ });
        });
        await page.GotoAsync("http://example.test/");
        var driver = new StandardCrudPageDriver(page);
        var row = await driver.WaitForRowAsync("Example record");
        await driver.InspectAndSaveObjectAuthorizationAsync(row, "example.record", CancellationToken.None);
        Assert.Equal(1, await page.EvaluateAsync<int>("window.inspectionClosed"));
        Assert.Equal(new[] { "GET " + target + "/inspection/explanation", "GET " + target, "PUT " + target }, requests);
        await Assertions.Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "待补齐页driver等待本次pending响应完成后才开始默认UI断言，旧请求或非匹配响应不能解锁，无服务器或端口")]
    public async Task OwnershipInitialization_WaitsForItsOwnPendingResponseBeforeTheDefaultUiAssertion()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        var oldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        await page.RouteAsync("http://workspace.test/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/workspace/ownership/pending" && route.Request.Method == "GET")
            {
                if (++reads == 1) { oldStarted.TrySetResult(); await oldReady.Task; }
                else { targetStarted.TrySetResult(); oldReady.TrySetResult(); await targetReady.Task; }
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
            }
            else if (path.StartsWith("/api/", StringComparison.Ordinal))
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
            else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><button id="group">授权与归属</button><div role="menu" hidden><button role="menuitem">待补齐归属</button></div>
                <script>
                  fetch('/api/workspace/ownership/pending');
                  document.querySelector('#group').onclick=()=>document.querySelector('[role=menu]').hidden=false;
                  document.querySelector('[role=menuitem]').onclick=async()=>{
                    document.querySelector('[role=menu]').hidden=true;
                    const modal=document.createElement('div'); modal.className='ant-modal'; modal.role='dialog';
                    modal.innerHTML='<button disabled>选择当前许可页</button>'; document.body.append(modal);
                    await fetch('/api/workspace/ownership/pending',{method:'POST'});
                    await fetch('/api/workspace/ownership/status');
                    await fetch('/api/workspace/ownership/pending');
                    modal.querySelector('button').disabled=false;
                  };
                </script>
                """ });
        });
        await page.GotoAsync("http://workspace.test/workspace");
        await oldStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        async Task OpenAndCheckAsync()
        {
            var dialog = await new WorkspacePageDriver(page).OpenOwnershipInitializationAsync();
            await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "选择当前许可页", Exact = true })).ToBeEnabledAsync();
        }
        var opening = OpenAndCheckAsync();
        await targetStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            // 与实际Native相同的默认5秒断言不能吞掉真实响应耗时；只延迟受控响应，不修改等待阈值。
            var first = await Task.WhenAny(opening, Task.Delay(TimeSpan.FromSeconds(6)));
            if (ReferenceEquals(opening, first))
            {
                await opening;
                Assert.Fail("本次pending响应完成前不能交给默认UI可用性断言。");
            }
        }
        finally { targetReady.TrySetResult(); oldReady.TrySetResult(); }
        await opening;
        Assert.Equal(2, reads);
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "待补齐页读取失败保留明确错误，不把HTTP失败当作可交互输入，无服务器或端口")]
    public async Task OwnershipInitialization_RejectsAnUnsuccessfulPendingResponse()
    {
        const int status = 403;
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
            if (new Uri(route.Request.Url).AbsolutePath == "/api/workspace/ownership/pending")
                await route.FulfillAsync(new() { Status = status, ContentType = "application/json", Body = "{}" });
            else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><button id="group">授权与归属</button><div role="menu" hidden><button role="menuitem">待补齐归属</button></div>
                <script>
                  document.querySelector('#group').onclick=()=>document.querySelector('[role=menu]').hidden=false;
                  document.querySelector('[role=menuitem]').onclick=()=>{
                    document.querySelector('[role=menu]').hidden=true;
                    const modal=document.createElement('div'); modal.className='ant-modal'; modal.role='dialog';
                    modal.innerHTML='<button disabled>选择当前许可页</button>'; document.body.append(modal);
                    fetch('/api/workspace/ownership/pending');
                  };
                </script>
                """ });
        });
        await page.GotoAsync("http://workspace.test/workspace");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkspacePageDriver(page).OpenOwnershipInitializationAsync());
        Assert.Contains("待补齐归属读取失败", exception.Message);
        Assert.Contains(status.ToString(), exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LostBatchReply_ExecutesOnceAndOnlyAbortsAfterSuccessfulCommit(bool succeeded)
    {
        var calls = new List<string>();
        Task<bool> Execute() { calls.Add("execute"); return Task.FromResult(succeeded); }
        Task Abort() { calls.Add("abort"); return Task.CompletedTask; }
        if (succeeded)
        {
            await WorkspacePageDriver.DropSuccessfulBatchReplyAsync(Execute, Abort);
            Assert.Equal(new[] { "execute", "abort" }, calls);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspacePageDriver.DropSuccessfulBatchReplyAsync(Execute, Abort));
            Assert.Equal(new[] { "execute" }, calls);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "独立Part片段等正式列表初载后选择真实新建按钮并保存，无服务器或端口")]
    public async Task IndependentPartCreate_WaitsForTheInitialListBeforeChoosingTheCreateEntry()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        await page.RouteAsync("http://workspace.test/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/ready")
            {
                requested.TrySetResult();
                await ready.Task;
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
            }
            else if (path == "/api/part/parts")
            {
                Assert.Equal("POST", route.Request.Method);
                writes++;
                await route.FulfillAsync(new() { Status = 201, ContentType = "application/json", Body = "{}" });
            }
            else if (path == "/parts") await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><main>列表加载中</main><script>
                  fetch('/ready').then(()=>{
                    document.querySelector('main').innerHTML='<div role="toolbar"><button id="create">新建零组件</button></div><table><tbody class="ant-table-tbody"><tr class="ant-table-row"><td>已有零组件</td></tr></tbody></table>';
                    document.querySelector('#create').onclick=()=>{
                      const drawer=document.createElement('div'); drawer.className='ant-drawer-content';
                      drawer.innerHTML='<label>编码<input></label><label>名称<input id="name"></label><button>保存并关闭</button>'; document.body.append(drawer);
                      drawer.querySelector('button').onclick=async()=>{
                        const name=drawer.querySelector('#name').value;
                        await fetch('/api/part/parts',{method:'POST'});
                        const row=document.createElement('tr'); row.className='ant-table-row';
                        const cell=document.createElement('td'); cell.textContent=name; row.append(cell);
                        document.querySelector('tbody').append(row); drawer.remove();
                      };
                    };
                  });
                </script>
                """ });
            else await route.FulfillAsync(new() { ContentType = "text/html", Body = "<main>工作台</main>" });
        });
        await page.GotoAsync("http://workspace.test/");
        var creating = AcceptanceGateTests.CreateIndependentPartAsync(page, "controlled");
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            // 初载未释放时只有占位，没有可选按钮或菜单；不修改driver的等待阈值。
            var first = await Task.WhenAny(creating, Task.Delay(TimeSpan.FromSeconds(6)));
            Assert.NotSame(creating, first);
            Assert.Equal(0, writes);
        }
        finally { ready.TrySetResult(); }
        await creating;
        Assert.Equal(1, writes);
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "树编辑driver先等本次保存后的目标路径读取，旧请求和其他节点不能提前完成，无服务器或端口")]
    public async Task TreeEdit_WaitsForItsSavedTargetPathBeforeCheckingTheBreadcrumb()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        var oldRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pathRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetReads = 0;
        await page.RouteAsync("http://workspace.test/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/trees/workspace.structure/path")
            {
                if (new Uri(route.Request.Url).Query.Contains("nodeKey=selected", StringComparison.Ordinal))
                {
                    if (++targetReads == 1) await oldRead.Task;
                    else { pathRequested.TrySetResult(); oldRead.TrySetResult(); await ready.Task; }
                }
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
            }
            else if (route.Request.Method == "PUT")
            {
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"succeeded\":true}" });
            }
            else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><aside aria-label="结构树"><span id="folder">原目录</span></aside>
                <section aria-label="节点内容"><nav aria-label="面包屑"><span aria-current="page">原目录</span></nav><table class="ant-table"><tr><td>内容</td></tr></table></section>
                <div role="menu" hidden><button role="menuitem">编辑</button></div>
                <script>
                  document.querySelector('#folder').oncontextmenu=event=>{ event.preventDefault(); document.querySelector('[role=menu]').hidden=false; };
                  document.querySelector('[role=menuitem]').onclick=()=>{
                    fetch('/api/trees/workspace.structure/path?nodeKey=selected');
                    document.querySelector('[role=menu]').hidden=true;
                    const drawer=document.createElement('div'); drawer.className='ant-drawer-content';
                    drawer.innerHTML='<label>名称<input value="原目录"></label><button>保存并关闭</button>'; document.body.append(drawer);
                    drawer.querySelector('button').onclick=async()=>{
                      const name=drawer.querySelector('input').value;
                      await fetch('/api/workspace/content/link',{method:'PUT',body:JSON.stringify({values:{ENAME:name}})});
                      drawer.remove();
                      await fetch('/api/trees/workspace.structure/path?nodeKey=another');
                      await fetch('/api/trees/workspace.structure/path?nodeKey=selected');
                      document.querySelector('[aria-current=page]').textContent=name;
                    };
                  };
                </script>
                """ });
        });
        await page.GotoAsync("http://workspace.test/workspace?node=selected");
        var editing = new WorkspacePageDriver(page).EditFolderFromTreeContextMenuAsync("原目录", "新目录");
        await pathRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            // 真实读取尚未完成时旧driver会在原5秒UI断言处失败；不改变任何等待阈值。
            var first = await Task.WhenAny(editing, Task.Delay(TimeSpan.FromSeconds(6)));
            if (ReferenceEquals(editing, first))
            {
                await editing;
                Assert.Fail("目标路径读取完成前不能进入最终面包屑断言。");
            }
        }
        finally { ready.TrySetResult(); }
        await editing;
        Assert.Equal(2, targetReads);
    }

    [EnvironmentGatedFact("NEWP_RUN_ACCEPTANCE", "恢复树位置先等待既有面包屑就绪，不把异步空快照当作最终内容，无服务器或端口")]
    public async Task RestorePosition_WaitsForTheRestoredBreadcrumbBeforeCheckingContent()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_CHROMIUM_EXECUTABLE")
                ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        });
        var page = await browser.NewPageAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("http://workspace.test/**", async route =>
        {
            if (new Uri(route.Request.Url).AbsolutePath == "/ready")
            {
                requested.TrySetResult();
                await ready.Task;
                await route.FulfillAsync(new() { Body = "{}", ContentType = "application/json" });
            }
            else await route.FulfillAsync(new() { ContentType = "text/html", Body = """
                <meta charset="utf-8"><aside aria-label="结构树">暂无节点</aside>
                <section aria-label="节点内容">请选择节点</section>
                <script>
                  fetch('/ready').then(()=>{
                    document.querySelector('section').innerHTML='<nav aria-label="面包屑"><button>公共空间</button><span aria-current="page">恢复目录</span></nav><table class="ant-table"><tr><td>内容</td></tr></table>';
                  });
                </script>
                """ });
        });
        var restoring = new WorkspacePageDriver(page).RestorePositionAsync("http://workspace.test/workspace?node=restored", "公共空间", "恢复目录");
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            // 原文本断言在5秒内失败；控制只延迟准备，未改变driver或Playwright的任何超时。
            var first = await Task.WhenAny(restoring, Task.Delay(TimeSpan.FromSeconds(6)));
            if (ReferenceEquals(restoring, first))
            {
                await restoring;
                Assert.Fail("恢复准备未释放前不应完成内容断言。");
            }
        }
        finally { ready.TrySetResult(); }
        await restoring;
        await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "节点内容" }).Locator(".ant-table")).ToBeVisibleAsync();
    }

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
                <meta charset="utf-8"><button id="paste">粘贴</button><div role="menu" hidden>
                <button role="menuitem" data-kind="copy">粘贴文件夹副本</button><button role="menuitem" data-kind="copy-structure">粘贴文件夹结构</button></div>
                <table><tbody class="ant-table-tbody"></tbody></table>
                <script>
                  document.querySelector('#paste').onclick=()=>document.querySelector('[role=menu]').hidden=false;
                  document.querySelectorAll('[data-kind]').forEach(button=>button.onclick=async()=>{
                    document.querySelector('[role=menu]').hidden=true;
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
                <section aria-label="节点内容"><nav aria-label="面包屑"><span aria-current="page">共享目录</span></nav>
                  <table class="ant-table"><tr><td>内容</td></tr></table></section>
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
                    <button id="remove">移除与删除</button><div role="menu" hidden><button id="batch" role="menuitem">ACTION_LABEL</button></div><table><tbody class="ant-table-tbody">
                    <tr class="ant-table-row"><td><input type="checkbox"></td><td>甲对象</td></tr>
                    <tr class="ant-table-row"><td><input type="checkbox"></td><td>乙对象</td></tr>
                    </tbody></table></section>
                    <div role="dialog" aria-label="删除" hidden><button id="confirm">确 认</button></div>
                    <script>
                    document.querySelector('#remove').onclick=()=>document.querySelector('[role=menu]').hidden=false;
                    document.querySelector('#batch').onclick=()=>{
                      document.querySelector('[role=menu]').hidden=true;
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
                    <button id="remove">移除与删除</button><div role="menu" hidden><button id="batch" role="menuitem">ACTION_LABEL</button></div><table><tbody class="ant-table-tbody">
                    <tr class="ant-table-row"><td><input type="checkbox"></td><td>甲对象</td></tr>
                    <tr class="ant-table-row"><td><input type="checkbox"></td><td>乙对象</td></tr>
                    </tbody></table></section>
                    <div role="dialog" hidden>仅从当前文件夹移除所选引用，保留对象及文件，确定继续？<button id="confirm">确 认</button></div>
                    <script>
                    document.querySelector('#remove').onclick=()=>document.querySelector('[role=menu]').hidden=false;
                    document.querySelector('#batch').onclick=()=>{
                      document.querySelector('[role=menu]').hidden=true;
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
              <nav aria-label="面包屑"><button>公共空间</button><button id="ancestor">路径A</button><span aria-current="page">后代T</span></nav>
            </header></section>
            <script>document.querySelector('#ancestor').onclick=()=>document.querySelector('[aria-current="page"]').textContent='路径A';</script>
            """);
        var driver = new StandardTreePageDriver(page);
        await driver.WaitForBreadcrumbAsync("公共空间", "路径A", "后代T");
        await driver.ClickAncestorAsync("路径A");
        await Assertions.Expect(page.GetByRole(AriaRole.Region).Locator("[aria-current=\"page\"]")).ToHaveTextAsync("路径A");
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
        page.SetDefaultTimeout(1_000);
        // 已选根只有当前面包屑，不会再发选中请求；双层 loading 仍须分别完成。
        await page.SetContentAsync("""
            <aside aria-label="结构树">公共空间</aside>
            <section aria-label="节点内容"><nav aria-label="面包屑"><span aria-current="page">公共空间</span></nav>
              <table class="ant-table"><tr><td>列表</td></tr></table>
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
