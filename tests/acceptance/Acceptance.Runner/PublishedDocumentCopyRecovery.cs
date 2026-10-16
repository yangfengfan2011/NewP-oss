using System.Diagnostics;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.Data.SqlClient;

namespace ICP.Platform.Acceptance;

internal sealed partial class PublishedComponentAcceptanceFixture
{
    // 只操作本fixture拥有的真实Host和storage root；字节事件定位中断，不向生产Host注入测试分支。
    private async Task<WorkspaceAcceptanceActor> VerifyDocumentCopyProcessRecoveryAsync(WorkspaceAcceptanceActor actor,
        Action<string> registerSecret, IReadOnlyList<FeaturePoint> granted, CancellationToken token)
    {
        using var client = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(client, actor, token);
        var features = await AcceptanceFeatureCatalog.LoadAsync(client, token);
        var workspace = features.Behavior("Workspace", "workspace.spaces.access");
        var entry = workspace.Position;
        var create = features.Behavior("Workspace", "workspace.folder.createDocument").Position;
        var copy = features.Action(workspace, "pasteObjectCopy", "list.pageActions", "folder").Position;
        var result = features.Action(workspace, "resolveObjectCopy", "list.pageActions", "folder").Position;
        var roots = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/api/workspace/spaces", null, entry, token);
        var root = roots.EnumerateArray().Single(row => row.GetProperty("spaceKind").GetString()!.Equals("public", StringComparison.OrdinalIgnoreCase));
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var folder = await WorkspaceAcceptanceFixture.CreateFolderAtPositionAsync(client, features,
            new(root.GetProperty("spaceId").GetString()!, rootId, rootId, []), "副本进程恢复 " + runId[..8], token);
        long[] lengths = [100L * 1024 * 1024, 8L * 1024 * 1024, 8L * 1024 * 1024];
        var values = new { ECODE = "OS-COPY-SOURCE-" + runId[..8], ENAME = "真实文件副本来源 " + runId[..8] };
        var uploads = new List<string>();
        for (var index = 0; index < lengths.Length; index++)
        {
            using var generated = new GeneratedUploadStream(lengths[index]);
            var staged = await StageRecoveryFileAsync(client, create, folder.CommandPosition, values, new StreamContent(generated), $"copy-process-{index}.pdf", token);
            if (generated.BytesRead != lengths[index]) throw new InvalidOperationException("副本恢复源文件未完整上传。");
            uploads.Add(staged.GetProperty("uploadId").GetString()!);
        }
        await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Post, "/api/workspace/content/documents", new { position = folder.CommandPosition, values,
            uploads = new { mainUploadId = uploads[0], attachmentUploadIds = uploads.Skip(1).ToArray() } }, create, token);
        var contents = await WorkspaceAcceptanceFixture.ReadContentsAtPositionAsync(client, folder, entry, token);
        var source = contents.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("fields").GetProperty("Name").GetString() == values.ENAME);
        var sourceId = source.GetProperty("associatedObject").GetProperty("id").GetString()!;
        var sourceLink = source.GetProperty("linkId").GetString()!;
        var sourceFacts = await ReadSavedFileFactsAsync(sourceId, token);
        var phases = exerciseDocumentCopyInterruptions ? new[] { "receiving", "content-complete", "prepared" } : [];
        foreach (var phase in phases)
        {
            using var active = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(active, actor, token);
            var preparation = await WorkspaceAcceptanceFixture.SendAsync(active, HttpMethod.Post, "/api/workspace/content/objects/copy/prepare",
                new { mode = "copy", sourcePosition = folder.CommandPosition, linkIds = new[] { sourceLink }, targetPosition = folder.CommandPosition }, copy, token);
            var operation = preparation.GetProperty("operationId").GetString()!;
            var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ownedHost = _host!.Process; var observedOnce = 0;
            using var watcher = new FileSystemWatcher(StorageRoot) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
            void Observe(string path)
            {
                if (Interlocked.CompareExchange(ref observedOnce, 1, 0) != 0) return;
                try { if (phase != "prepared") ownedHost.Kill(entireProcessTree: true); observed.TrySetResult(path); }
                catch (Exception failure) { observed.TrySetException(failure); }
            }
            if (phase == "content-complete") watcher.Renamed += (_, change) => { if (!change.FullPath.EndsWith(".part", StringComparison.Ordinal)) Observe(change.FullPath); };
            else watcher.Created += (_, change) => { if (change.FullPath.EndsWith(".part", StringComparison.Ordinal)) Observe(change.FullPath); };
            watcher.EnableRaisingEvents = true;
            var previous = HostIdentity;
            var pending = WorkspaceAcceptanceFixture.SendAsync(active, HttpMethod.Post, "/api/workspace/content/objects/copy",
                new { operationId = operation, values = new { ECODE = "OS-COPY-" + phase + "-" + runId[..8], ENAME = "中断副本 " + phase } }, copy, token);
            var observedPath = await observed.Task.WaitAsync(TimeSpan.FromSeconds(60), token);
            watcher.EnableRaisingEvents = false;
            if (phase == "prepared")
            {
                // 第一份文件已开始IO才取得图锁，既不阻塞短prepare事务，也不把字节传输移入图事务。
                await using var blocker = new SqlConnection(_database!.ConnectionString); await blocker.OpenAsync(token);
                await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync(token);
                await using (var command = new SqlCommand("DECLARE @r int; EXEC @r=sp_getapplock @Resource=N'Workspace.FolderGraph',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=1000; IF @r<0 THROW 51000,'Copy phase barrier failed.',1;", blocker, transaction))
                    await command.ExecuteNonQueryAsync(token);
                await WaitForCopyRowsAsync(operation, rows => rows.Length == 3 && rows.All(row => row.GetProperty("leaseState").GetString() == "prepared"), token);
                await StopHostAsync(token); await transaction.RollbackAsync(token);
            }
            else await StopHostAsync(token);
            try { await pending; throw new InvalidOperationException("被中断的副本请求意外成功。"); }
            catch (Exception failure) when (failure is HttpRequestException or OperationCanceledException) { }
            var durable = await ReadCopyRowsAsync(operation, token);
            if (durable.Length == 0 || phase != "prepared" && durable[0].GetProperty("leaseState").GetString() != "receiving")
                throw new InvalidOperationException("进程中断未命中所声明的持久复制阶段。");
            if (phase == "content-complete" && !File.Exists(observedPath)) throw new InvalidOperationException("完成阶段未留下真实已完成内容。");
            await StartHostAsync(hostRoot, "copy-recovery-" + phase, token);
            if (HostIdentity == previous) throw new InvalidOperationException("副本恢复没有启动新Host进程。");
            var restartedAfterInterruption = HostIdentity;
            if (phase == "prepared" && (await ReadCopyRowsAsync(operation, token)).Any(row => row.GetProperty("leaseState").GetString() != "prepared"))
                throw new InvalidOperationException("重启错误删除合法prepared内容。");
            using var resumed = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(resumed, actor, token);
            var receipt = await WorkspaceAcceptanceFixture.SendAsync(resumed, HttpMethod.Post, "/api/workspace/content/objects/copy/result", new { operationId = operation }, result, token);
            if (receipt.GetProperty("succeeded").GetBoolean()) throw new InvalidOperationException("未提交的进程中断被错误报告成功。");
            var cleanupWatch = Stopwatch.StartNew();
            var cancelled = await WorkspaceAcceptanceFixture.SendAsync(resumed, HttpMethod.Post, "/api/workspace/content/objects/copy/cancel", new { operationId = operation }, copy, token);
            if (cancelled.GetProperty("state").GetString() != "cancelled") throw new InvalidOperationException("重启未提交准备不能受控取消。");
            // 复用正式启动扫描触发确定性回收；不为测试改变五分钟产品周期或引入清理HTTP旁路。
            await RestartAsync(token);
            var cleaned = await WaitForCopyRowsAsync(operation, rows => rows.All(row => row.GetProperty("fileState").GetString() == "deleted"
                && !File.Exists(RecoveryPath(row.GetProperty("storageKey").GetString()!)) && !File.Exists(RecoveryPath(row.GetProperty("storageKey").GetString()!) + ".part")), token);
            if (sourceFacts != await ReadSavedFileFactsAsync(sourceId, token)) throw new InvalidOperationException("复制中断恢复改变已保存来源。");
            cleanupWatch.Stop();
            await WriteAsync("document-copy-process-" + phase + ".json", new { phase, previous, restartedAfterInterruption, cleanupRestarted = HostIdentity,
                operation, observedPath, durable, receipt, cancelled, cleaned, cancellationAndStartupReclaimMs = cleanupWatch.Elapsed.TotalMilliseconds, sourceUnchanged = true }, token);
        }
        return await VerifyCommittedDocumentCopyAsync(actor, folder, sourceLink, sourceFacts, sourceId, lengths, features, registerSecret, granted, token);
    }

    // 已提交复制使用真实文件做成本样本；随后硬进程重启及父模块停用/恢复都不得损坏使用或字节。
    private async Task<WorkspaceAcceptanceActor> VerifyCommittedDocumentCopyAsync(WorkspaceAcceptanceActor actor, WorkspaceFolderPosition folder, string sourceLink,
        string sourceFacts, string sourceId, long[] lengths, AcceptanceFeatureCatalog features, Action<string> registerSecret,
        IReadOnlyList<FeaturePoint> granted, CancellationToken token)
    {
        using var client = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(client, actor, token);
        var workspace = features.Behavior("Workspace", "workspace.spaces.access");
        var copy = features.Action(workspace, "pasteObjectCopy", "list.pageActions", "folder").Position;
        var result = features.Action(workspace, "resolveObjectCopy", "list.pageActions", "folder").Position;
        var preparation = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Post, "/api/workspace/content/objects/copy/prepare",
            new { mode = "copy", sourcePosition = folder.CommandPosition, linkIds = new[] { sourceLink }, targetPosition = folder.CommandPosition }, copy, token);
        var operation = preparation.GetProperty("operationId").GetString()!;
        var baselineBytes = Directory.EnumerateFiles(StorageRoot).Sum(path => new FileInfo(path).Length);
        var samples = new List<CopyResourceSample>(); using var sampling = new CancellationTokenSource();
        var monitor = SampleCopyResourcesAsync(samples, sampling.Token); var watch = Stopwatch.StartNew();
        JsonElement committed;
        try { committed = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Post, "/api/workspace/content/objects/copy",
            new { operationId = operation, values = new { ECODE = "OS-COPY-COMMITTED-" + runId[..8], ENAME = "进程重启完整副本 " + runId[..8] } }, copy, token); }
        finally { watch.Stop(); sampling.Cancel(); await monitor; }
        if (!committed.GetProperty("succeeded").GetBoolean()) throw new InvalidOperationException("真实文件副本未提交。");
        var rows = await ReadCopyRowsAsync(operation, token);
        await WriteAsync("document-copy-real-file-cost.json", new { operation, host = HostIdentity, fileCount = lengths.Length, lengths, totalBytes = lengths.Sum(),
            baselineBytes, samples, elapsedMs = watch.Elapsed.TotalMilliseconds, bytesPerSecond = lengths.Sum() / watch.Elapsed.TotalSeconds, rows,
            measurement = "Real published Host; 20ms private/working/storage samples; copy elapsed excludes upload/download and lifecycle restarts." }, token);
        // 按本fixture确认的唯一编码定位，不猜测列表第一项，也不从安全HTTP回执取不可读身份。
        var audit = await ReadSqlJsonAsync("SELECT JSON_VALUE(Input,'$.SourceId') sourceId FROM dbo.WorkspaceObjectCopy WHERE EID=@id FOR JSON PATH;", operation, token);
        var copyDocument = await ReadSqlJsonAsync("SELECT EID id FROM dbo.Document WHERE ECODE=@id FOR JSON PATH;", "OS-COPY-COMMITTED-" + runId[..8], token);
        using var copyJson = JsonDocument.Parse(copyDocument); var copyId = copyJson.RootElement.EnumerateArray().Single().GetProperty("id").GetString()!;
        var before = await ReadSavedFileFactsAsync(copyId, token); var previous = HostIdentity;
        await RestartAsync(token);
        using var restarted = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(restarted, actor, token);
        var receipt = await WorkspaceAcceptanceFixture.SendAsync(restarted, HttpMethod.Post, "/api/workspace/content/objects/copy/result", new { operationId = operation }, result, token);
        if (receipt.GetProperty("state").GetString() != "committed" || before != await ReadSavedFileFactsAsync(copyId, token)) throw new InvalidOperationException("新进程未保留已提交图。");
        await VerifyGeneratedCopyDownloadsAsync(restarted, copyId, lengths, features, token);
        await WriteAsync("document-copy-process-committed.json", new { previous, restarted = HostIdentity, operation, copyId, receipt, before, preserved = true }, token);
        await StopHostAsync(token); await RunOfflineAsync("disable", ["--module-id", "InforCenter"], token); await StartHostAsync(hostRoot, "copy-parent-disabled", token);
        using (var disabled = WorkspaceAcceptanceFixture.CreateClient(_hostPort))
        {
            var runtime = await WorkspaceAcceptanceFixture.SendAsync(disabled, HttpMethod.Get, "/plugins/active-module-runtime.json", null, null, token);
            var active = runtime.GetProperty("modules").EnumerateArray().Select(row => row.GetProperty("moduleId").GetString()).ToArray();
            if (active.Contains("Document") || active.Contains("Workspace") || !active.Contains("Files")) throw new InvalidOperationException("模块停用未命中Document/Workspace或Files被误停用。");
        }
        if (before != await ReadSavedFileFactsAsync(copyId, token)) throw new InvalidOperationException("停用期间清理改变确认使用。");
        await StopHostAsync(token); await RunOfflineAsync("enable", ["--module-id", "InforCenter", "--version", "1.0.0"], token); await StartHostAsync(hostRoot, "copy-parent-restored", token);
        // 正式停用撤销旧功能授权；恢复内容不等于恢复旧角色。按既有fixture建立显式新有限角色。
        using var pageAdministrator = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(pageAdministrator, _administrator!, token);
        var restoredFeatures = await AcceptanceFeatureCatalog.LoadAsync(pageAdministrator, token);
        var restoredActor = await WorkspaceAcceptanceFixture.PrepareAsync(pageAdministrator, restoredFeatures, "restored-" + runId, registerSecret, granted, token);
        using var restored = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(restored, restoredActor, token);
        await VerifyGeneratedCopyDownloadsAsync(restored, copyId, lengths, restoredFeatures, token);
        if (before != await ReadSavedFileFactsAsync(copyId, token) || sourceFacts != await ReadSavedFileFactsAsync(sourceId, token)) throw new InvalidOperationException("模块恢复改变来源或副本。");
        await WriteAsync("document-copy-process-committed-cost.json", new { previous, restarted = HostIdentity, operation, copyId, rows, receipt, audit,
            previousActorId = actor.AccountId, restoredActorId = restoredActor.AccountId, oldGrantsRestored = false,
            fileCount = lengths.Length, lengths, totalBytes = lengths.Sum(), baselineBytes, samples, elapsedMs = watch.Elapsed.TotalMilliseconds,
            bytesPerSecond = lengths.Sum() / watch.Elapsed.TotalSeconds, before, moduleDisableRestore = true, sourceUnchanged = true,
            measurement = "Real published Host process; sampled private/working bytes and storage files; upload and download excluded from copy elapsed." }, token);
        return restoredActor;
    }

    private sealed record CopyResourceSample(DateTime At, long PrivateBytes, long WorkingBytes, long DiskBytes, long PartialBytes);

    // 采样只读本Host进程及自身root；不遍历其他存储、不用分配总量代替常驻内存。
    private async Task SampleCopyResourcesAsync(List<CopyResourceSample> samples, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            _host!.Process.Refresh(); var files = Directory.EnumerateFiles(StorageRoot).Select(path => new FileInfo(path)).ToArray();
            long disk = 0, partial = 0;
            foreach (var file in files) { try { var size = file.Length; disk += size; if (file.Name.EndsWith(".part", StringComparison.Ordinal)) partial += size; } catch (FileNotFoundException) { } }
            samples.Add(new(DateTime.UtcNow, _host.Process.PrivateMemorySize64, _host.Process.WorkingSet64, disk, partial));
            try { await Task.Delay(20, token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    // 每个恢复样本只按持久request key查询自己的lease及目标；取消墓碑没有物理内容，不伪造文件归属。
    private async Task<JsonElement[]> ReadCopyRowsAsync(string operation, CancellationToken token)
    {
        using var json = JsonDocument.Parse(await ReadSqlJsonAsync("SELECT l.State leaseState,l.RequestKey,l.SourceFileId,l.ConfirmedUsageId,f.EID fileId,f.State fileState,f.StorageKey storageKey,f.Length,f.Sha256 FROM dbo.FileCopyLease l JOIN dbo.FileRecord f ON f.EID=l.FileId WHERE l.RequestKey LIKE @id ORDER BY l.RequestKey FOR JSON PATH;", operation + ".%", token));
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    // 观察有界期限内真实持久状态，超时保持异常，不能将未观察阶段写为已覆盖。
    private async Task<JsonElement[]> WaitForCopyRowsAsync(string operation, Func<JsonElement[], bool> ready, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (true) { var rows = await ReadCopyRowsAsync(operation, timeout.Token); if (rows.Length > 0 && ready(rows)) return rows; await Task.Delay(50, timeout.Token); }
    }

    // 下载按独立已知的A字节生成样本逐缓冲核对，不将元数据hash当成内容正确的唯一依据。
    private async Task VerifyGeneratedCopyDownloadsAsync(HttpClient client, string id, long[] lengths, AcceptanceFeatureCatalog features, CancellationToken token)
    {
        var read = features.Behavior("Document", "document.document.read").Position; var download = features.Behavior("Document", "document.document.download").Position;
        var files = (await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, $"/api/document/documents/{id}/files", null, read, token)).GetProperty("files").EnumerateArray().ToArray();
        if (files.Length != lengths.Length) throw new InvalidOperationException("真实进程恢复丢失主附件。");
        for (var index = 0; index < files.Length; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/document/documents/{id}/files/{files[index].GetProperty("fileLinkId").GetString()}/content"); request.Headers.Add("X-NewP-Feature", download);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
            await using var content = await response.Content.ReadAsStreamAsync(token); var buffer = new byte[65536]; long received = 0; int count;
            while ((count = await content.ReadAsync(buffer, token)) != 0) { if (buffer.AsSpan(0, count).ContainsAnyExcept((byte)0x41)) throw new InvalidOperationException("重启后的副本内容不同。"); received += count; }
            if (received != lengths[index]) throw new InvalidOperationException("重启后的副本文件不完整。");
        }
    }
}
