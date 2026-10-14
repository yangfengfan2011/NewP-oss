using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;

namespace ICP.Platform.Acceptance;

// 正式进程、HTTP 与真实 SQL 的文件恢复旅程；不通过测试 Host 或手工建表替代发布输入。
internal sealed partial class PublishedComponentAcceptanceFixture
{
    private sealed record RecoveryFile(string FileId, string UploadId, string FileState, string LeaseState, string StorageKey);

    private async Task VerifyFileProcessRecoveryAsync(WorkspaceAcceptanceActor actor, CancellationToken token)
    {
        using var client = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(client, actor, token);
        var features = await AcceptanceFeatureCatalog.LoadAsync(client, token);
        var entry = features.Behavior("Workspace", "workspace.spaces.access").Position;
        var create = features.Behavior("Workspace", "workspace.folder.createDocument").Position;
        var read = features.Behavior("Document", "document.document.read").Position;
        var download = features.Behavior("Document", "document.document.download").Position;
        var roots = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/api/workspace/spaces", null, entry, token);
        var root = roots.EnumerateArray().Single(row => row.GetProperty("spaceKind").GetString()!.Equals("public", StringComparison.OrdinalIgnoreCase));
        var position = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
        await VerifyKestrelUploadBoundaryAsync(client, create, position, token);
        var name = "正式文件重启 " + runId[..8];
        var values = new { ECODE = "OS-MAIN-" + runId[..8], ENAME = name };
        byte[] bytes = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x00, 0xff];
        var staged = await StageRecoveryFileAsync(client, create, position, values, new ByteArrayContent(bytes), "saved-" + runId[..8] + ".pdf", token);
        await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Post, "/api/workspace/content/documents",
            new { position, values, uploads = new { mainUploadId = staged.GetProperty("uploadId").GetString() } }, create, token);
        var content = await WorkspaceAcceptanceFixture.ReadRootContentsAsync(client, root, entry, token);
        var documentId = content.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("fields").GetProperty("Name").GetString() == name)
            .GetProperty("associatedObject").GetProperty("id").GetString()!;
        var before = await ReadSavedFileFactsAsync(documentId, token);
        await VerifyRecoveryDownloadAsync(client, documentId, read, download, bytes, token);
        var previous = HostIdentity;
        await RestartAsync(token);
        if (HostIdentity == previous) throw new InvalidOperationException("文件恢复未启动新的正式 Host 进程。");
        using var restarted = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(restarted, actor, token);
        await VerifyRecoveryDownloadAsync(restarted, documentId, read, download, bytes, token);
        var after = await ReadSavedFileFactsAsync(documentId, token);
        if (before != after) throw new InvalidOperationException("同库同 root 重启改变了已保存的 Document/文件关联或使用。");
        await WriteAsync("files-saved-process-restart.json", new { previous, restarted = HostIdentity, catalog = ReusableSqlServerDatabase.Catalog,
            storageRoot = StorageRoot, documentId, before, after, length = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }, token);

        await VerifyReceivingProcessInterruptionAsync(restarted, actor, create, position, token);
        using var finalClient = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(finalClient, actor, token);
        await VerifyStagedAndPendingRecoveryAsync(finalClient, actor, create, position, token);
        using var savedClient = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(savedClient, actor, token);
        await VerifyRecoveryDownloadAsync(savedClient, documentId, read, download, bytes, token);
        if (before != await ReadSavedFileFactsAsync(documentId, token)) throw new InvalidOperationException("恢复未确认文件时改变了 active 文件使用。");
    }

    private async Task VerifyKestrelUploadBoundaryAsync(HttpClient client, string create, object position, CancellationToken token)
    {
        const long maximum = 104857600;
        var acceptedName = "limit-" + runId[..8] + ".pdf";
        using var acceptedSource = new GeneratedUploadStream(maximum);
        var accepted = await StageRecoveryFileAsync(client, create, position,
            new { ECODE = "OS-LIMIT-" + runId[..8], ENAME = "真实HTTP上限" }, new StreamContent(acceptedSource), acceptedName, token);
        if (acceptedSource.BytesRead != maximum || accepted.GetProperty("length").GetInt64() != maximum)
            throw new InvalidOperationException("真实Kestrel请求没有将完整100MiB传到文件服务。");
        var ready = await WaitForRecoveryFileAsync(acceptedName, file => file.FileState == "ready" && file.LeaseState == "staged", token);
        var stored = await ReadSqlJsonAsync("SELECT Length,Sha256 FROM dbo.FileRecord WHERE EID=@id FOR JSON PATH;", ready.FileId, token);
        using (var facts = JsonDocument.Parse(stored))
            if (facts.RootElement[0].GetProperty("Length").GetInt64() != maximum)
                throw new InvalidOperationException("真实HTTP上限文件持久长度不符。");

        var rejectedName = "limit-plus-one-" + runId[..8] + ".pdf";
        using var rejectedSource = new GeneratedUploadStream(maximum + 1);
        using var request = await RecoveryUploadRequestAsync(client, create, position,
            new { ECODE = "OS-OVER-" + runId[..8], ENAME = "真实HTTP超限" }, new StreamContent(rejectedSource), rejectedName, token);
        using var response = await client.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        using var error = JsonDocument.Parse(text);
        if (response.StatusCode != HttpStatusCode.RequestEntityTooLarge || error.RootElement.GetProperty("code").GetString() != "Files.UploadTooLarge")
            throw new InvalidOperationException($"真实Kestrel上限多1byte没有按413拒绝：{(int)response.StatusCode} {_sanitize(text)}");
        var rejected = await ReadRecoveryFileAsync(rejectedName, token);
        if (rejected is { FileState: "ready" } || rejected is { LeaseState: "staged" or "confirmed" })
            throw new InvalidOperationException("真实HTTP超限请求留下可消费文件。");
        await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Delete, "/api/workspace/content/document-uploads/" + accepted.GetProperty("uploadId").GetString(), null, create, token);
        await WriteAsync("files-kestrel-upload-boundary.json", new { maximum, acceptedBytesRead = acceptedSource.BytesRead,
            accepted, ready, stored, rejectedBytesRead = rejectedSource.BytesRead, rejectedStatus = (int)response.StatusCode, rejected,
            nonseek = !acceptedSource.CanSeek && !rejectedSource.CanSeek, storageRoot = StorageRoot }, token);
    }

    private async Task<JsonElement> StageRecoveryFileAsync(HttpClient client, string feature, object position, object values,
        HttpContent file, string fileName, CancellationToken token)
    {
        using var request = await RecoveryUploadRequestAsync(client, feature, position, values, file, fileName, token);
        using var response = await client.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"正式上传失败 {(int)response.StatusCode}: {_sanitize(text)}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private async Task<HttpRequestMessage> RecoveryUploadRequestAsync(HttpClient client, string feature, object position, object values,
        HttpContent file, string fileName, CancellationToken token)
    {
        using var antiforgery = await client.GetAsync("/api/auth/antiforgery-token", token);
        antiforgery.EnsureSuccessStatusCode();
        using var proof = JsonDocument.Parse(await antiforgery.Content.ReadAsStringAsync(token));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads");
        request.Headers.Add("X-NewP-Feature", feature);
        request.Headers.Add("X-CSRF-TOKEN", proof.RootElement.GetProperty("requestToken").GetString());
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(position), Encoding.UTF8, "application/json"), "position");
        multipart.Add(new StringContent(JsonSerializer.Serialize(values), Encoding.UTF8, "application/json"), "values");
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(file, "file", fileName);
        request.Content = multipart;
        return request;
    }

    private async Task VerifyRecoveryDownloadAsync(HttpClient client, string documentId, string read, string download, byte[] bytes, CancellationToken token)
    {
        var metadata = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, $"/api/document/documents/{documentId}/files", null, read, token);
        var item = metadata.GetProperty("files").EnumerateArray().Single();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/document/documents/{documentId}/files/{item.GetProperty("fileLinkId").GetString()}/content");
        request.Headers.Add("X-NewP-Feature", download);
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var received = await response.Content.ReadAsByteArrayAsync(token);
        if (!bytes.SequenceEqual(received) || response.Content.Headers.ContentLength != bytes.Length
            || response.Content.Headers.ContentDisposition?.DispositionType != "attachment")
            throw new InvalidOperationException("正式重启下载字节、长度或 attachment 契约不符。");
    }

    private Task<string> ReadSavedFileFactsAsync(string id, CancellationToken token) => ReadSqlJsonAsync(
        "SELECT d.EID documentId,d.LatestVersionId,v.CurrentIterationId,l.LID fileLinkId,l.PEID,l.CEID,l.FileUsageId,l.Role,l.Ordinal," +
        "u.OwnerKey,u.OwnerObjectId,u.FileId,u.State usageState,f.State fileState,f.Length,f.Sha256,s.EID uploadId,s.State leaseState,s.ConfirmedUsageId " +
        "FROM dbo.Document d JOIN dbo.DocumentVersion v ON v.EID=d.LatestVersionId JOIN dbo.DocumentIteration i ON i.EID=v.CurrentIterationId " +
        "JOIN dbo.DocumentFileLink l ON l.PEID=i.EID JOIN dbo.FileUsage u ON u.EID=l.FileUsageId JOIN dbo.FileRecord f ON f.EID=u.FileId " +
        "JOIN dbo.FileUploadLease s ON s.ConfirmedUsageId=u.EID WHERE d.EID=@id ORDER BY l.Ordinal,l.LID FOR JSON PATH;", id, token);

    private async Task VerifyReceivingProcessInterruptionAsync(HttpClient client, WorkspaceAcceptanceActor actor, string create, object position, CancellationToken token)
    {
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var prefix = new InterruptedUploadContent(stopped.Token);
        var fileName = "receiving-" + runId[..8] + ".pdf";
        using var request = await RecoveryUploadRequestAsync(client, create, position,
            new { ECODE = "OS-RECV-" + runId[..8], ENAME = "中断上传 " + runId[..8] }, prefix, fileName, token);
        var upload = client.SendAsync(request, stopped.Token);
        Exception? scenarioFailure = null;
        try
        {
            var receiving = await WaitForRecoveryFileAsync(fileName, file => file.FileState == "receiving"
                && File.Exists(RecoveryPath(file.StorageKey) + ".part") && new FileInfo(RecoveryPath(file.StorageKey) + ".part").Length > 0, token);
            var previous = HostIdentity;
            await StopHostAsync(token);
            stopped.Cancel();
            try
            {
                using var response = await upload.WaitAsync(TimeSpan.FromSeconds(10));
                if (response.IsSuccessStatusCode) throw new InvalidOperationException("中断的 receiving 上传意外发布成功。");
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException) { }
            await StartHostAsync(hostRoot, "receiving-recovery-host", token);
            var deleted = await WaitForRecoveryFileAsync(fileName, file => file.FileState == "deleted" && file.LeaseState == "cleaned", token);
            RequireContentAbsent(deleted);
            await WriteAsync("files-receiving-process-recovery.json", new { previous, restarted = HostIdentity, actorId = actor.AccountId,
                storageRoot = StorageRoot, receiving, deleted, partialRemoved = true }, token);
        }
        catch (Exception exception) { scenarioFailure = exception; throw; }
        finally
        {
            // Dispose CTS 不会取消；即使观察先失败也必须停 producer 并观察后台任务，原场景异常优先。
            try
            {
                stopped.Cancel();
                using var response = await upload.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException) { }
            catch (Exception exception) when (scenarioFailure is not null)
            { scenarioFailure.Data["InterruptedUploadCleanupFailure"] = exception.GetType().Name; }
        }
    }

    private async Task VerifyStagedAndPendingRecoveryAsync(HttpClient client, WorkspaceAcceptanceActor actor, string create, object position, CancellationToken token)
    {
        var fileName = "staged-" + runId[..8] + ".pdf";
        var staged = await StageRecoveryFileAsync(client, create, position, new { ECODE = "OS-STAGED-" + runId[..8], ENAME = "暂存重启" },
            new ByteArrayContent([1, 2, 3]), fileName, token);
        var original = await WaitForRecoveryFileAsync(fileName, file => file.LeaseState == "staged", token);
        await RestartAsync(token);
        var retained = await ReadRecoveryFileAsync(fileName, token);
        if (original != retained || !File.Exists(RecoveryPath(original!.StorageKey))) throw new InvalidOperationException("合法 staged lease 在进程重启时被错误回收。");
        // 等价注入本 fixture lease 的服务器到期事实；不等待24小时、不改用户数据或生产 TTL。
        await using (var connection = new SqlConnection(_database!.ConnectionString))
        {
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dbo.FileUploadLease SET ExpiresAt=DATEADD(second,-1,SYSUTCDATETIME()) WHERE EID=@id AND State=N'staged';";
            command.Parameters.AddWithValue("@id", staged.GetProperty("uploadId").GetString());
            if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("暂存到期注入未精确命中自身lease。");
        }
        await RestartAsync(token);
        var expired = await WaitForRecoveryFileAsync(fileName, file => file.FileState == "deleted" && file.LeaseState == "cleaned", token);
        RequireContentAbsent(expired);
        await WriteAsync("files-staged-process-recovery.json", new { original, retained, expired, storageRoot = StorageRoot }, token);

        using var pendingClient = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(pendingClient, actor, token);
        var pendingName = "pending-" + runId[..8] + ".pdf";
        var pendingStage = await StageRecoveryFileAsync(pendingClient, create, position, new { ECODE = "OS-PENDING-" + runId[..8], ENAME = "回收重试" },
            new ByteArrayContent([4, 5, 6]), pendingName, token);
        var activeStage = await WaitForRecoveryFileAsync(pendingName, file => file.LeaseState == "staged", token);
        RecoveryFile failed;
        using (var held = new FileStream(RecoveryPath(activeStage.StorageKey), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await WorkspaceAcceptanceFixture.SendAsync(pendingClient, HttpMethod.Delete,
                "/api/workspace/content/document-uploads/" + pendingStage.GetProperty("uploadId").GetString(), null, create, token);
            await RestartAsync(token);
            await WaitForCleanupFailureAsync(activeStage.FileId, token);
            failed = await WaitForRecoveryFileAsync(pendingName, file => file.FileState == "cleanupPending", token);
            if (!File.Exists(RecoveryPath(failed.StorageKey))) throw new InvalidOperationException("真实IO失败却提前删除了待回收内容。");
        }
        await RestartAsync(token);
        var recovered = await WaitForRecoveryFileAsync(pendingName, file => file.FileState == "deleted" && file.LeaseState == "cleaned", token);
        RequireContentAbsent(recovered);
        await WriteAsync("files-pending-process-recovery.json", new { failed, recovered, ioFailureObserved = true, storageRoot = StorageRoot }, token);
    }

    private async Task<RecoveryFile?> ReadRecoveryFileAsync(string fileName, CancellationToken token)
    {
        var json = await ReadSqlJsonAsync("SELECT f.EID fileId,l.EID uploadId,f.State fileState,l.State leaseState,f.StorageKey storageKey " +
            "FROM dbo.FileRecord f JOIN dbo.FileUploadLease l ON l.FileId=f.EID WHERE f.FileName=@id FOR JSON PATH;", fileName, token);
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.EnumerateArray().ToArray();
        if (rows.Length == 0) return null;
        if (rows.Length != 1) throw new InvalidOperationException("文件恢复样本名称没有唯一命中自身事实。");
        var row = rows[0];
        return new(row.GetProperty("fileId").GetString()!, row.GetProperty("uploadId").GetString()!, row.GetProperty("fileState").GetString()!,
            row.GetProperty("leaseState").GetString()!, row.GetProperty("storageKey").GetString()!);
    }

    private async Task<RecoveryFile> WaitForRecoveryFileAsync(string fileName, Func<RecoveryFile, bool> ready, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            var file = await ReadRecoveryFileAsync(fileName, timeout.Token);
            if (file is not null && ready(file)) return file;
            await Task.Delay(100, timeout.Token);
        }
    }

    private async Task WaitForCleanupFailureAsync(string fileId, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!_host!.Lines.Any(line => line.Contains("Files cleanup deferred", StringComparison.Ordinal) && line.Contains(fileId, StringComparison.Ordinal)))
        {
            if (_host.Process.HasExited) throw new InvalidOperationException("正式Host在观察真实IO失败前退出。");
            await Task.Delay(100, timeout.Token);
        }
    }

    private string RecoveryPath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(StorageRoot, key));
        if (!path.StartsWith(Path.GetFullPath(StorageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("文件恢复样本路径离开自身私有root。");
        return path;
    }

    private void RequireContentAbsent(RecoveryFile file)
    {
        var path = RecoveryPath(file.StorageKey);
        if (File.Exists(path) || File.Exists(path + ".part")) throw new InvalidOperationException("正式启动恢复后仍遗留自身登记内容。");
    }

    // Multipart 使用未知长度、只送有界前缀的真实HTTP内容；进程中断前保留服务端活动writer。
    private sealed class InterruptedUploadContent(CancellationToken stopped) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            using var producer = CancellationTokenSource.CreateLinkedTokenSource(stopped, token);
            await stream.WriteAsync(new byte[131072], producer.Token);
            await stream.FlushAsync(producer.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, producer.Token);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    // HttpContent 以未知长度发送单趟流；生成器只填调用方缓冲区，不分配完整100MiB文件。
    private sealed class GeneratedUploadStream(long length) : Stream
    {
        internal long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, length - BytesRead);
            buffer[..count].Fill(0x41);
            BytesRead += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Read(buffer.Span)); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
