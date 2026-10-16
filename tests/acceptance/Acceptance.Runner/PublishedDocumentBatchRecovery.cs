using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace ICP.Platform.Acceptance;

internal sealed partial class PublishedComponentAcceptanceFixture
{
    // 已有单文件各阶段由#291证明；这里命中首文档已prepared、后文档receiving的跨文档窗口并中断真实自有PID。
    private async Task VerifyDocumentBatchProcessRecoveryAsync(WorkspaceAcceptanceActor actor, CancellationToken token)
    {
        using var client = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(client, actor, token);
        var features = await AcceptanceFeatureCatalog.LoadAsync(client, token);
        var workspace = features.Behavior("Workspace", "workspace.spaces.access"); var entry = workspace.Position;
        var create = features.Behavior("Workspace", "workspace.folder.createDocument").Position;
        var copy = features.Action(workspace, "pasteObjectCopy", "list.pageActions", "folder").Position;
        var result = features.Action(workspace, "resolveObjectCopy", "list.pageActions", "folder").Position;
        var roots = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/api/workspace/spaces", null, entry, token);
        var root = roots.EnumerateArray().Single(row => row.GetProperty("spaceKind").GetString()!.Equals("public", StringComparison.OrdinalIgnoreCase));
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var folder = await WorkspaceAcceptanceFixture.CreateFolderAtPositionAsync(client, features,
            new(root.GetProperty("spaceId").GetString()!, rootId, rootId, []), "集合进程恢复 " + runId[..8], token);
        long[] lengths = [8L * 1024 * 1024, 2L * 1024 * 1024]; var sourceLinks = new List<string>(); var sourceIds = new List<string>();
        for (var document = 0; document < 2; document++)
        {
            var values = new { ECODE = "OS-BATCH-SOURCE-" + document + "-" + runId[..8], ENAME = "集合进程来源 " + document + " " + runId[..8] };
            var uploads = new List<string>();
            for (var file = 0; file < lengths.Length; file++)
            {
                using var generated = new GeneratedUploadStream(lengths[file]);
                var uploaded = await StageRecoveryFileAsync(client, create, folder.CommandPosition, values, new StreamContent(generated), $"batch-process-{document}-{file}.pdf", token);
                if (generated.BytesRead != lengths[file]) throw new InvalidOperationException("集合进程来源上传未完整消费。");
                uploads.Add(uploaded.GetProperty("uploadId").GetString()!);
            }
            await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Post, "/api/workspace/content/documents", new { position = folder.CommandPosition, values,
                uploads = new { mainUploadId = uploads[0], attachmentUploadIds = uploads.Skip(1).ToArray() } }, create, token);
            var contents = await WorkspaceAcceptanceFixture.ReadContentsAtPositionAsync(client, folder, entry, token);
            var row = contents.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("fields").GetProperty("Name").GetString() == values.ENAME);
            sourceLinks.Add(row.GetProperty("linkId").GetString()!); sourceIds.Add(row.GetProperty("associatedObject").GetProperty("id").GetString()!);
        }
        var sourceFacts = new List<string>(); foreach (var id in sourceIds) sourceFacts.Add(await ReadSavedFileFactsAsync(id, token));
        var baselineKeys = Directory.EnumerateFiles(StorageRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prepared = await Prepare(client); var operation = prepared.GetProperty("operationId").GetString()!;
        var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var ownedHost = _host!.Process; var previous = HostIdentity; var receivingOrdinal = 0;
        using (var watcher = new FileSystemWatcher(StorageRoot) { NotifyFilter = NotifyFilters.FileName })
        {
            watcher.Created += (_, change) => {
                if (!change.FullPath.EndsWith(".part", StringComparison.Ordinal) || !seen.TryAdd(change.FullPath, 0) || Interlocked.Increment(ref receivingOrdinal) != 3) return;
                try { ownedHost.Kill(entireProcessTree: true); observed.TrySetResult(change.FullPath); } catch (Exception failure) { observed.TrySetException(failure); }
            };
            watcher.EnableRaisingEvents = true;
            var pending = WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Post, "/api/workspace/content/objects/copy", new { operationId = operation, values = BatchValues("OS-BATCH-INTERRUPTED") }, copy, token);
            var observedPath = await observed.Task.WaitAsync(TimeSpan.FromSeconds(60), token); watcher.EnableRaisingEvents = false;
            await StopHostAsync(token);
            try { await pending; throw new InvalidOperationException("跨文档中断请求意外提交。"); } catch (Exception failure) when (failure is HttpRequestException or OperationCanceledException) { }
            var durable = await ReadCopyRowsAsync(operation, token);
            if (durable.Count(row => row.GetProperty("RequestKey").GetString()!.StartsWith(operation + ".0.", StringComparison.Ordinal)
                    && row.GetProperty("leaseState").GetString() == "prepared") != 2
                || !durable.Any(row => row.GetProperty("RequestKey").GetString()!.StartsWith(operation + ".1.", StringComparison.Ordinal)
                    && row.GetProperty("leaseState").GetString() == "receiving")) throw new InvalidOperationException("中断没有命中前文档准备完成、后文档接收的真实窗口。");
            await StartHostAsync(hostRoot, "batch-partial-restarted", token);
            if (HostIdentity == previous) throw new InvalidOperationException("集合恢复未启动新PID。");
            using var resumed = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(resumed, actor, token);
            var receipt = await WorkspaceAcceptanceFixture.SendAsync(resumed, HttpMethod.Post, "/api/workspace/content/objects/copy/result", new { operationId = operation }, result, token);
            if (receipt.GetProperty("succeeded").GetBoolean() || receipt.GetProperty("items").GetArrayLength() != 2) throw new InvalidOperationException("中断集合被报告部分成功。");
            var reclaim = Stopwatch.StartNew();
            var cancelled = await WorkspaceAcceptanceFixture.SendAsync(resumed, HttpMethod.Post, "/api/workspace/content/objects/copy/cancel", new { operationId = operation }, copy, token);
            if (cancelled.GetProperty("state").GetString() != "cancelled") throw new InvalidOperationException("未提交集合不能整体取消。");
            await RestartAsync(token);
            var cleaned = await WaitForCopyRowsAsync(operation, rows => rows.All(row => row.GetProperty("fileState").GetString() == "deleted"
                && !File.Exists(RecoveryPath(row.GetProperty("storageKey").GetString()!)) && !File.Exists(RecoveryPath(row.GetProperty("storageKey").GetString()!) + ".part")), token);
            reclaim.Stop();
            if (!baselineKeys.SetEquals(Directory.EnumerateFiles(StorageRoot))) throw new InvalidOperationException("集合取消回收遗留文件或误删其他内容。");
            for (var index = 0; index < sourceIds.Count; index++) if (sourceFacts[index] != await ReadSavedFileFactsAsync(sourceIds[index], token)) throw new InvalidOperationException("集合中断恢复改变来源。");
            await WriteAsync("document-batch-partial-process-recovery.json", new { previous, restarted = HostIdentity, operation, observedPath, durable, receipt, cancelled,
                cleaned, cancellationAndStartupReclaimMs = reclaim.Elapsed.TotalMilliseconds, sourceUnchanged = true }, token);
        }
        using var committing = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(committing, actor, token);
        prepared = await Prepare(committing); operation = prepared.GetProperty("operationId").GetString()!;
        var watch = Stopwatch.StartNew();
        var committed = await WorkspaceAcceptanceFixture.SendAsync(committing, HttpMethod.Post, "/api/workspace/content/objects/copy", new { operationId = operation, values = BatchValues("OS-BATCH-COPY") }, copy, token);
        watch.Stop();
        if (!committed.GetProperty("succeeded").GetBoolean() || committed.GetProperty("items").GetArrayLength() != 2) throw new InvalidOperationException("集合未完整提交。");
        var copyIds = new List<string>(); var copyFacts = new List<string>();
        for (var index = 0; index < 2; index++)
        {
            using var saved = JsonDocument.Parse(await ReadSqlJsonAsync("SELECT EID id FROM dbo.Document WHERE ECODE=@id FOR JSON PATH;", "OS-BATCH-COPY-" + index + "-" + runId[..8], token));
            var id = saved.RootElement.EnumerateArray().Single().GetProperty("id").GetString()!; copyIds.Add(id); copyFacts.Add(await ReadSavedFileFactsAsync(id, token));
        }
        previous = HostIdentity; await RestartAsync(token);
        using var restarted = WorkspaceAcceptanceFixture.CreateClient(_hostPort); await WorkspaceAcceptanceFixture.LoginAsync(restarted, actor, token);
        var recovered = await WorkspaceAcceptanceFixture.SendAsync(restarted, HttpMethod.Post, "/api/workspace/content/objects/copy/result", new { operationId = operation }, result, token);
        var lateCancel = await WorkspaceAcceptanceFixture.SendAsync(restarted, HttpMethod.Post, "/api/workspace/content/objects/copy/cancel", new { operationId = operation }, copy, token);
        if (recovered.GetProperty("state").GetString() != "committed" || lateCancel.GetProperty("state").GetString() != "committed" || HostIdentity == previous)
            throw new InvalidOperationException("重启/迟到取消未保留已提交集合。");
        for (var index = 0; index < 2; index++)
        {
            if (copyFacts[index] != await ReadSavedFileFactsAsync(copyIds[index], token) || sourceFacts[index] != await ReadSavedFileFactsAsync(sourceIds[index], token))
                throw new InvalidOperationException("集合重启改变来源/副本图。");
            await VerifyGeneratedCopyDownloadsAsync(restarted, copyIds[index], lengths, features, token);
        }
        await WriteAsync("document-batch-committed-process-recovery.json", new { previous, restarted = HostIdentity, operation, committed, recovered, lateCancel,
            documents = 2, files = 4, lengths, totalBytes = 2 * lengths.Sum(), copyIds, copyFacts, elapsedMs = watch.Elapsed.TotalMilliseconds, sourceUnchanged = true }, token);

        // 正式输入/结果始终按同一受控位置；旧UploadId或临时文件清单不参与恢复。
        Task<JsonElement> Prepare(HttpClient active) => WorkspaceAcceptanceFixture.SendAsync(active, HttpMethod.Post, "/api/workspace/content/objects/copy/prepare",
            new { mode = "copy", sourcePosition = folder.CommandPosition, linkIds = sourceLinks.ToArray(), targetPosition = folder.CommandPosition }, copy, token);
        object BatchValues(string prefix) => new { items = sourceLinks.Select((link, index) => new { sourceLinkId = link,
            values = new { ECODE = prefix + "-" + index + "-" + runId[..8], ENAME = "集合恢复副本 " + index + " " + runId[..8] } }).ToArray() };
    }
}