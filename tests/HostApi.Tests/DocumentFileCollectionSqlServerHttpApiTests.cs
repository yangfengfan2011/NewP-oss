using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

/// <summary>集合验收沿真实 Workspace HTTP、Document owner、SQL 事务和 Local 内容验证。</summary>
public sealed class DocumentFileCollectionSqlServerHttpApiTests(ITestOutputHelper output)
{
    private const string SqlGate = "NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION";

    [EnvironmentGatedFact(SqlGate, "主文件与两个附件共享一次业务事务，首个FileLink保存前使用完整，较小Files批量读取仍闭合")]
    public async Task MainAndTwoAttachments_CommitAllFactsAndReadDownloadInOrder()
    {
        var save = new CompleteCollectionSave();
        await using var fixture = await FixtureAsync(save, 2);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await RootAsync(fixture, admin);
        var values = new { ECODE = "COLLECTION-01", ENAME = "三文件图文档" };
        byte[][] contents = [[0x25, 0x50, 0x44, 0x46, 1], [2, 3, 4], [5, 6, 7, 8]];
        var staged = new JsonElement[3];
        for (var index = 0; index < staged.Length; index++)
            staged[index] = await StageAsync(fixture, admin, position, values, contents[index], $"file-{index}.pdf");
        using var created = await SaveAsync(fixture, admin, position, values, staged);
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var receipt = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.Single(receipt.EnumerateObject());
        Assert.True(save.ObservedCompleteAddedCollection);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var master = await db.Set<Dictionary<string, object?>>("Document").AsNoTracking().SingleAsync();
        var version = await db.Set<Dictionary<string, object?>>("DocumentVersion").AsNoTracking().SingleAsync();
        var iteration = await db.Set<Dictionary<string, object?>>("DocumentIteration").AsNoTracking().SingleAsync();
        Assert.Equal(version["EID"], master["LatestVersionId"]);
        Assert.Equal(master["EID"], version["DocumentId"]);
        Assert.Equal(iteration["EID"], version["CurrentIterationId"]);
        Assert.Equal(version["EID"], iteration["VersionId"]);
        var links = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().OrderBy(row => EF.Property<long>(row, "Ordinal")).ToArrayAsync();
        var usages = await db.Set<Dictionary<string, object?>>("FileUsage").AsNoTracking().ToArrayAsync();
        var leases = await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().ToArrayAsync();
        var files = await db.Set<Dictionary<string, object?>>("FileRecord").AsNoTracking().ToArrayAsync();
        Assert.Equal(3, links.Length); Assert.Equal(3, usages.Length); Assert.Equal(3, leases.Length); Assert.Equal(3, files.Length);
        Assert.Equal(master["EID"], (await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync())["CEID"]);
        var documentId = (string)master["EID"]!;
        var list = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "document.document.read"), $"/api/document/documents/{documentId}/files");
        var publicFiles = list.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(3, publicFiles.Length);
        for (var index = 0; index < links.Length; index++)
        {
            var link = links[index]; var fileId = staged[index].GetProperty("fileId").GetString();
            var usage = Assert.Single(usages, row => Equals(row["EID"], link["FileUsageId"]));
            var lease = Assert.Single(leases, row => Equals(row["EID"], staged[index].GetProperty("uploadId").GetString()));
            var file = Assert.Single(files, row => Equals(row["EID"], fileId));
            Assert.Equal(iteration["EID"], link["PEID"]); Assert.Equal(fileId, link["CEID"]);
            Assert.Equal(index == 0 ? "main" : "attachment", link["Role"]); Assert.Equal((long)index, link["Ordinal"]);
            Assert.Equal(fileId, usage["FileId"]); Assert.Equal(link["LID"], usage["OwnerObjectId"]);
            Assert.Equal("document.fileLink", usage["OwnerKey"]); Assert.Equal("active", usage["State"]);
            Assert.Equal(usage["EID"], lease["ConfirmedUsageId"]); Assert.Equal("confirmed", lease["State"]);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(contents[index])), file["Sha256"]);
            Assert.Equal((long)contents[index].Length, file["Length"]);
            Assert.Equal(link["LID"], publicFiles[index].GetProperty("fileLinkId").GetString());
            Assert.Equal(index, publicFiles[index].GetProperty("ordinal").GetInt32());
            Assert.Equal($"file-{index}.pdf", publicFiles[index].GetProperty("fileName").GetString());
            foreach (var hidden in new[] { "fileId", "usageId", "storageKey", "actorId" }) Assert.False(publicFiles[index].TryGetProperty(hidden, out _));
            using var downloaded = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "document.document.download"), HttpMethod.Get,
                $"/api/document/documents/{documentId}/files/{link["LID"]}/content");
            Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
            Assert.Equal(contents[index], await downloaded.Content.ReadAsByteArrayAsync());
            Assert.Equal(contents[index].Length, downloaded.Content.Headers.ContentLength);
        }
    }

    [EnvironmentGatedFact(SqlGate, "主附件全局canonical token与配置上限在任何业务写入前核验")]
    public async Task UploadSet_RejectsGlobalDuplicatesCanonicalVariantsAndConfiguredOverflowWithoutConsumption()
    {
        await using var fixture = await FixtureAsync(maxAttachments: 2);
        using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        var values = Values("SET-SHAPE"); var staged = await StageThreeAsync(fixture, admin, position, values);
        var main = Token(staged[0]); var first = Token(staged[1]); var second = Token(staged[2]);
        object[] invalid = [
            new { mainUploadId = "", attachmentUploadIds = new[] { first } },
            new { mainUploadId = (string?)null, attachmentUploadIds = new[] { first } },
            new { mainUploadId = main.ToUpperInvariant(), attachmentUploadIds = new[] { first } },
            new { mainUploadId = main + "\n", attachmentUploadIds = new[] { first } },
            new { mainUploadId = main, attachmentUploadIds = new[] { main } },
            new { mainUploadId = main, attachmentUploadIds = new[] { first, first } },
            new { mainUploadId = main, attachmentUploadIds = new[] { first, " " } },
            new { mainUploadId = main, attachmentUploadIds = new string?[] { first, null } },
            new { mainUploadId = main, attachmentUploadIds = new[] { first, second + "\n" } },
            new { mainUploadId = main, attachmentUploadIds = new[] { first, second, "FileUploadLease_00000000000000000000000000000000" } }
        ];
        foreach (var uploads in invalid)
        {
            using var rejected = await RawSaveAsync(fixture, admin, position, values, uploads);
            await ProblemAsync(rejected, HttpStatusCode.BadRequest, "Document.InvalidUploadSet");
        }
        await AssertUnconsumedAsync(fixture, 3);
        using var accepted = await SaveAsync(fixture, admin, position, values, staged);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [EnvironmentGatedFact(SqlGate, "跨Files小批量仍先核整集合actor和用途，不能暴露本人已失效状态")]
    public async Task SmallBatches_WholeSetIdentityPrecedesExpiredOrCancelledStateInEitherRequestOrder()
    {
        await using var fixture = await FixtureAsync(batchSize: 1);
        using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        var values = Values("SET-IDENTITY"); var staged = await StageThreeAsync(fixture, admin, position, values);
        await ChangeAsync(fixture, "FileUploadLease", "EID", Token(staged[0]), "ExpiresAt", DateTime.UtcNow.AddMinutes(-1));
        foreach (var state in new[] { "expired", "cancelled" })
        {
            if (state == "cancelled")
            {
                await ChangeAsync(fixture, "FileUploadLease", "EID", Token(staged[0]), "ExpiresAt", DateTime.UtcNow.AddHours(1));
                using var cancelled = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Delete,
                    "/api/workspace/content/document-uploads/" + Token(staged[0]));
                Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
            }
            foreach (var identity in new[] { "ActorId", "UsageOwnerKey" })
            {
                var actual = identity == "ActorId" ? fixture.ActorId : DocumentFileOwnerPolicy.Key;
                await ChangeAsync(fixture, "FileUploadLease", "EID", Token(staged[2]), identity, "another-identity");
                foreach (var set in new[] { staged, new[] { staged[2], staged[1], staged[0] } })
                {
                    using var denied = await SaveAsync(fixture, admin, position, values, set);
                    await ProblemAsync(denied, HttpStatusCode.NotFound, "Files.UploadUnavailable");
                }
                await ChangeAsync(fixture, "FileUploadLease", "EID", Token(staged[2]), identity, actual);
            }
            using var ownState = await SaveAsync(fixture, admin, position, values, staged);
            await ProblemAsync(ownState, HttpStatusCode.Conflict, state == "expired" ? "Files.UploadExpired" : "Files.UploadCancelled");
        }
        Assert.Empty(await RowsAsync(fixture, "Document")); Assert.Empty(await RowsAsync(fixture, "FileUsage"));
        Assert.Equal("staged", Assert.Single(await RowsAsync(fixture, "FileUploadLease"), row => Equals(row["EID"], Token(staged[1])))["State"]);
    }

    [EnvironmentGatedFact(SqlGate, "第二与末项真实File锁处失败，先前登记使用与全图回滚并保留staged重试")]
    public async Task SecondAndLastConfirmationFailure_RollsBackAllFactsAndAllowsSameSetRetry()
    {
        var fault = new CollectionSqlFault(); await using var fixture = await FixtureAsync(fault);
        using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        var values = Values("SET-CONFIRM-FAIL"); var staged = await StageThreeAsync(fixture, admin, position, values);
        // 请求特意反向排列FileId，真实SQL applock仍必须按Files身份排序，而显示顺序保持请求。
        staged = staged.OrderByDescending(item => item.GetProperty("fileId").GetString(), StringComparer.Ordinal).ToArray();
        foreach (var rejectedOrdinal in new[] { 2, 3 })
        {
            fault.ArmLockFailure(rejectedOrdinal);
            await InjectedFailureAsync(() => SaveAsync(fixture, admin, position, values, staged));
            Assert.Equal(rejectedOrdinal, fault.FileLocks.Count);
            Assert.Equal(rejectedOrdinal - 1, fault.ObservedActiveUsages);
            Assert.Equal(3, fault.ObservedAddedLinks);
            Assert.Equal(staged.Select(item => "Files." + item.GetProperty("fileId").GetString()).Order(StringComparer.Ordinal).Take(rejectedOrdinal), fault.FileLocks);
            await AssertUnconsumedAsync(fixture, 3);
        }
        fault.ObserveLocks();
        using var retry = await SaveAsync(fixture, admin, position, values, staged);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(staged.Select(item => "Files." + item.GetProperty("fileId").GetString()).Order(StringComparer.Ordinal), fault.FileLocks);
        var list = await ReadListAsync(fixture, admin, "SET-CONFIRM-FAIL");
        Assert.Equal(staged.Select(item => item.GetProperty("fileName").GetString()), list.GetProperty("files").EnumerateArray().Select(item => item.GetProperty("fileName").GetString()));
    }

    [EnvironmentGatedFact(SqlGate, "两个不同图文档同时消费相同集合时只允许一个整体提交")]
    public async Task ConcurrentDifferentTargets_ConsumeTheSameSetOnceWithoutPartialDocuments()
    {
        await using var fixture = await FixtureAsync(); using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        var staged = await StageThreeAsync(fixture, admin, position, Values("SET-RACE-STAGE"));
        var first = SaveAsync(fixture, admin, position, Values("SET-RACE-A"), staged);
        var second = SaveAsync(fixture, admin, position, Values("SET-RACE-B"), staged.Reverse().ToArray());
        var results = await Task.WhenAll(first, second);
        try
        {
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
            var denied = Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
            await ProblemAsync(denied, HttpStatusCode.Conflict, "Files.UploadAlreadyConfirmed");
        }
        finally { foreach (var result in results) result.Dispose(); }
        foreach (var model in new[] { "Document", "DocumentVersion", "DocumentIteration", "WorkspaceFolderContent" }) Assert.Single(await RowsAsync(fixture, model));
        Assert.Equal(3, (await RowsAsync(fixture, "DocumentFileLink")).Length);
        Assert.Equal(3, (await RowsAsync(fixture, "FileUsage")).Length);
        Assert.All(await RowsAsync(fixture, "FileUploadLease"), row => Assert.Equal("confirmed", row["State"]));
    }

    [EnvironmentGatedFact(SqlGate, "编码冲突与后续目录SQL写入失败都不消费集合，修正后可原集合重试")]
    public async Task DuplicateCodeAndWorkspaceReferenceFailure_LeaveTheEntireSetAvailable()
    {
        var fault = new CollectionSqlFault(); await using var fixture = await FixtureAsync(fault);
        using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        using (var existing = await RawSaveAsync(fixture, admin, position, Values("SET-DUPLICATE"), null)) Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
        var staged = await StageThreeAsync(fixture, admin, position, Values("SET-RETRY"));
        using (var duplicate = await SaveAsync(fixture, admin, position, Values("SET-DUPLICATE"), staged))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Contains("Document.Code", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        Assert.Empty(await RowsAsync(fixture, "DocumentFileLink")); Assert.Empty(await RowsAsync(fixture, "FileUsage"));
        fault.FailReference = true;
        await InjectedFailureAsync(() => SaveAsync(fixture, admin, position, Values("SET-RETRY"), staged));
        Assert.Equal(3, fault.ObservedActiveUsages); Assert.Equal(3, fault.ObservedAddedLinks);
        foreach (var model in new[] { "Document", "DocumentVersion", "DocumentIteration", "WorkspaceFolderContent" }) Assert.Single(await RowsAsync(fixture, model));
        Assert.Empty(await RowsAsync(fixture, "DocumentFileLink")); Assert.Empty(await RowsAsync(fixture, "FileUsage"));
        Assert.All(await RowsAsync(fixture, "FileUploadLease"), row => Assert.Equal("staged", row["State"]));
        using var retry = await SaveAsync(fixture, admin, position, Values("SET-RETRY"), staged); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [EnvironmentGatedFact(SqlGate, "提交前真实请求取消回滚三文件全图，合法内容仍可修正重试")]
    public async Task CancellationBeforeCommit_RollsBackTheWholeCollectionAndAllowsRetry()
    {
        var control = new CollectionCommitControl { Pause = true };
        await using var fixture = await FixtureAsync(control); using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        var staged = await StageThreeAsync(fixture, admin, position, Values("SET-CANCEL"));
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/documents")
        { Content = JsonContent.Create(SaveInput(position, Values("SET-CANCEL"), staged), options: new JsonSerializerOptions()) };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
        control.Armed = true; var saving = admin.SendAsync(request, cancellation.Token);
        try
        {
            await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await saving);
            await control.RolledBack.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { cancellation.Cancel(); control.Resume.TrySetResult(); try { (await saving).Dispose(); } catch (OperationCanceledException) { } }
        Assert.Equal(3, control.ObservedLinks); Assert.Equal(3, control.ObservedUsages);
        await AssertUnconsumedAsync(fixture, 3);
        using var retry = await SaveAsync(fixture, admin, position, Values("SET-CANCEL-RETRY"), staged); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [EnvironmentGatedFact(SqlGate, "已提交回执未知保留完整三文件持久事实与字节，不能重放消费")]
    public async Task UnknownAfterCommit_PreservesTheCompleteCollection()
    {
        var control = new CollectionCommitControl { ThrowAfterCommit = true };
        await using var fixture = await FixtureAsync(control); using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        var staged = await StageThreeAsync(fixture, admin, position, Values("SET-UNKNOWN")); control.Armed = true;
        HttpResponseMessage? response = null;
        try { response = await SaveAsync(fixture, admin, position, Values("SET-UNKNOWN"), staged); }
        catch (Exception exception) { Assert.Contains(nameof(InjectedCollectionFailure), exception.ToString(), StringComparison.Ordinal); }
        using (response) { if (response is not null) Assert.False(response.IsSuccessStatusCode); }
        Assert.Equal(1, control.Injected); Assert.Equal(3, control.ObservedLinks); Assert.Equal(3, control.ObservedUsages);
        Assert.Single(await RowsAsync(fixture, "Document")); Assert.Equal(3, (await RowsAsync(fixture, "DocumentFileLink")).Length);
        Assert.All(await RowsAsync(fixture, "FileUsage"), row => Assert.Equal("active", row["State"]));
        Assert.All(await RowsAsync(fixture, "FileUploadLease"), row => Assert.Equal("confirmed", row["State"]));
        Assert.Equal(3, (await ReadListAsync(fixture, admin, "SET-UNKNOWN")).GetProperty("files").GetArrayLength());
        using var replay = await SaveAsync(fixture, admin, position, Values("SET-UNKNOWN-REPLAY"), staged);
        await ProblemAsync(replay, HttpStatusCode.Conflict, "Files.UploadAlreadyConfirmed");
    }

    [EnvironmentGatedFact(SqlGate, "100附件上界通过真实Files服务与Document HTTP，101项按ordinal稳定读取且配置调低不破坏旧集合")]
    public async Task MaximumCollection_ReadsAll101InSmallBatchesAndSurvivesLowerCreationLimit()
    {
        var probe = new CollectionSqlFault();
        await using var fixture = await FixtureAsync(probe, batchSize: 7); using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        var staged = await StageThroughFilesAsync(fixture, 101);
        probe.ObserveStagedReads = true;
        using var saved = await SaveAsync(fixture, admin, position, Values("SET-MAXIMUM"), staged);
        probe.ObserveStagedReads = false;
        Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        Assert.Equal(15, probe.StagedLeaseBatchSizes.Count); Assert.Equal(101, probe.StagedLeaseBatchSizes.Sum());
        Assert.Equal(15, probe.StagedFileBatchSizes.Count); Assert.Equal(101, probe.StagedFileBatchSizes.Sum());
        Assert.All(probe.StagedLeaseBatchSizes.Concat(probe.StagedFileBatchSizes), size => Assert.InRange(size, 1, 7));
        output.WriteLine($"Staged set SQL: returned={staged.Length}; Files.MaxReadBatchSize=7; leaseQueries={probe.StagedLeaseBatchSizes.Count}; fileQueries={probe.StagedFileBatchSizes.Count}; largestLeaseBatch={probe.StagedLeaseBatchSizes.Max()}; largestFileBatch={probe.StagedFileBatchSizes.Max()}.");
        fixture.Services.GetRequiredService<IOptions<DocumentOptions>>().Value.MaxAttachmentsPerDocument = 0;
        var document = Assert.Single(await RowsAsync(fixture, "Document"));
        probe.ObserveMetadataReads = true;
        var response = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "document.document.read"), $"/api/document/documents/{document["EID"]}/files");
        probe.ObserveMetadataReads = false;
        var list = response.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(101, list.Length); Assert.Equal(Enumerable.Range(0, 101), list.Select(item => item.GetProperty("ordinal").GetInt32()));
        Assert.Equal(staged.Select(item => item.GetProperty("fileName").GetString()), list.Select(item => item.GetProperty("fileName").GetString()));
        Assert.Equal(15, probe.UsageReadBatchSizes.Count); Assert.Equal(101, probe.UsageReadBatchSizes.Sum());
        Assert.Equal(7, probe.UsageReadBatchSizes.Max()); Assert.All(probe.UsageReadBatchSizes, size => Assert.InRange(size, 1, 7));
        Assert.Equal(15, probe.RecordReads); Assert.Equal(15, probe.ConfirmedLeaseReads);
        output.WriteLine($"Collection metadata SQL: returned={list.Length}; Files.MaxReadBatchSize=7; totalCommands={probe.MetadataCommands}; usageQueries={probe.UsageReadBatchSizes.Count}; recordQueries={probe.RecordReads}; confirmedLeaseQueries={probe.ConfirmedLeaseReads}; largestUsageIdBatch={probe.UsageReadBatchSizes.Max()}; sumUsageIds={probe.UsageReadBatchSizes.Sum()}; Document.Take=102; absoluteReturnLimit=101.");
        using var last = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "document.document.download"), HttpMethod.Get,
            $"/api/document/documents/{document["EID"]}/files/{list[^1].GetProperty("fileLinkId").GetString()}/content");
        Assert.Equal(HttpStatusCode.OK, last.StatusCode); Assert.Equal(new byte[] { 100, 101, 102 }, await last.Content.ReadAsByteArrayAsync());
        var ws = Assert.Single(await RowsAsync(fixture, "WorkspaceFolderContent"));
        using var deleted = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + ws["LID"], new { position });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode); Assert.Empty(await RowsAsync(fixture, "DocumentFileLink"));
        Assert.All(await RowsAsync(fixture, "FileUsage"), row => Assert.Equal("released", row["State"]));
    }

    [EnvironmentGatedFact(SqlGate, "同图文档附件Usage互换、File互换与孤立回链不能通过read/download配对")]
    public async Task SameDocumentAssociationSwapsAndOrphanUsage_FailExplicitly()
    {
        await using var fixture = await FixtureAsync(); using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        var staged = await StageThreeAsync(fixture, admin, position, Values("SET-PAIR"));
        using (var saved = await SaveAsync(fixture, admin, position, Values("SET-PAIR"), staged)) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var document = Assert.Single(await RowsAsync(fixture, "Document")); var id = (string)document["EID"]!;
        var links = (await RowsAsync(fixture, "DocumentFileLink")).OrderBy(row => (long)row["Ordinal"]!).ToArray();
        var first = links[1]; var last = links[2];
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)first["LID"]!, "FileUsageId", "");
        await AssertReadAndDownloadCorruptedAsync(fixture, admin, id, (string)first["LID"]!);
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)first["LID"]!, "FileUsageId", first["FileUsageId"]);
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)first["LID"]!, "FileUsageId", last["FileUsageId"]);
        await AssertReadAndDownloadCorruptedAsync(fixture, admin, id, (string)first["LID"]!);
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)last["LID"]!, "FileUsageId", first["FileUsageId"]);
        await AssertReadAndDownloadCorruptedAsync(fixture, admin, id, (string)first["LID"]!);
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)first["LID"]!, "FileUsageId", first["FileUsageId"]);
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)last["LID"]!, "FileUsageId", last["FileUsageId"]);
        // 临时CEID仅存在于测试自有损坏注入中，使最终两个真实CEID能在unique约束下交换。
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)first["LID"]!, "CEID", "FileRecord_00000000000000000000000000000000");
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)last["LID"]!, "CEID", first["CEID"]);
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)first["LID"]!, "CEID", last["CEID"]);
        await AssertReadAndDownloadCorruptedAsync(fixture, admin, id, (string)first["LID"]!);
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)first["LID"]!, "CEID", "FileRecord_00000000000000000000000000000000");
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)last["LID"]!, "CEID", last["CEID"]);
        await ChangeAsync(fixture, "DocumentFileLink", "LID", (string)first["LID"]!, "CEID", first["CEID"]);
        await ChangeAsync(fixture, "FileUsage", "EID", (string)first["FileUsageId"]!, "OwnerObjectId", "DocumentFileLink_00000000000000000000000000000000");
        await AssertReadAndDownloadCorruptedAsync(fixture, admin, id, (string)first["LID"]!);
        await ChangeAsync(fixture, "FileUsage", "EID", (string)first["FileUsageId"]!, "OwnerObjectId", first["LID"]);
        Assert.Equal(3, (await ReadListAsync(fixture, admin, "SET-PAIR")).GetProperty("files").GetArrayLength());
    }

    [EnvironmentGatedFact(SqlGate, "集合单删与混合批删后项失败保全所有使用和字节，成功释放后cleanup幂等且保护剩余active与合法staged")]
    public async Task OwnAndMixedBulkDelete_RollBackEveryUsageThenCleanupReleasedOnly()
    {
        var fault = new CollectionSqlFault(); await using var fixture = await FixtureAsync(fault);
        using var admin = await fixture.LoginAdministratorAsync(); var position = await RootAsync(fixture, admin);
        foreach (var code in new[] { "SET-DELETE-A", "SET-DELETE-B" })
        {
            var staged = await StageThreeAsync(fixture, admin, position, Values(code));
            using var saved = await SaveAsync(fixture, admin, position, Values(code), staged); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }
        using (var part = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createPart"), HttpMethod.Post,
            "/api/workspace/content/parts", new { position, values = new { ECODE = "SET-DELETE-PART", ENAME = "混合零组件" } })) Assert.Equal(HttpStatusCode.OK, part.StatusCode);
        var spare = await StageAsync(fixture, admin, position, Values("SET-DELETE-SPARE"), [9, 8, 7], "spare.pdf");
        var ws = await RowsAsync(fixture, "WorkspaceFolderContent");
        var docIds = (await RowsAsync(fixture, "Document")).Select(row => (string)row["EID"]!).ToArray();
        var docLinks = ws.Where(row => docIds.Contains((string)row["CEID"]!)).ToArray();
        fault.ArmDeleteFailure(1);
        await InjectedFailureAsync(() => FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + docLinks[0]["LID"], new { position }));
        Assert.Equal(3, fault.ObservedReleasedUsages); await AssertActiveCollectionsAsync(fixture, 6);
        fault.ArmDeleteFailure(2);
        await InjectedFailureAsync(() => FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObjects"), HttpMethod.Post,
            "/api/workspace/content/delete", new { position, linkIds = new[] { (string)docLinks[0]["LID"]!, (string)ws.Single(row => !docIds.Contains((string)row["CEID"]!))["LID"]!, (string)docLinks[1]["LID"]! } }));
        Assert.Equal(6, fault.ObservedReleasedUsages); await AssertActiveCollectionsAsync(fixture, 6); Assert.Single(await RowsAsync(fixture, "Part"));
        using (var deleted = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + docLinks[0]["LID"], new { position })) Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(3, await CleanupAsync(fixture)); Assert.Equal(0, await CleanupAsync(fixture));
        Assert.Equal(3, (await RowsAsync(fixture, "FileUsage")).Count(row => Equals(row["State"], "active")));
        Assert.Equal("staged", Assert.Single(await RowsAsync(fixture, "FileUploadLease"), row => Equals(row["EID"], Token(spare)))["State"]);
        using (var deleted = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObjects"), HttpMethod.Post,
            "/api/workspace/content/delete", new { position, linkIds = ws.Where(row => !Equals(row["LID"], docLinks[0]["LID"])).Select(row => (string)row["LID"]!).ToArray() })) Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(3, await CleanupAsync(fixture)); Assert.Equal(0, await CleanupAsync(fixture));
        foreach (var model in new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "WorkspaceFolderContent", "Part" }) Assert.Empty(await RowsAsync(fixture, model));
        Assert.All(await RowsAsync(fixture, "FileUsage"), row => Assert.Equal("released", row["State"]));
        Assert.Single(Directory.EnumerateFiles(fixture.StorageRoot));
    }

    private static Task<BusinessAuditHostFixture> FixtureAsync(IInterceptor? interceptor = null, int batchSize = 128, int maxAttachments = 100) => BusinessAuditHostFixture.CreateAsync(services =>
    {
        services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(FileCleanupWorker)));
        services.Configure<FilesOptions>(options => options.MaxReadBatchSize = batchSize);
        services.Configure<DocumentOptions>(options => options.MaxAttachmentsPerDocument = maxAttachments);
        if (interceptor is not null) services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(interceptor));
    }, includeWorkspace: true);

    private static Task<HttpResponseMessage> SaveAsync(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition position, object values, JsonElement[] staged) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Post, "/api/workspace/content/documents", SaveInput(position, values, staged));

    private static object SaveInput(WorkspacePosition position, object values, JsonElement[] staged) =>
        new { position, values, uploads = new { mainUploadId = Token(staged[0]), attachmentUploadIds = staged.Skip(1).Select(Token).ToArray() } };
    private static Task<HttpResponseMessage> RawSaveAsync(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition position, object values, object? uploads) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Post, "/api/workspace/content/documents", new { position, values, uploads });
    private static object Values(string code) => new { ECODE = code, ENAME = "集合图文档-" + code };
    private static string Token(JsonElement staged) => staged.GetProperty("uploadId").GetString()!;
    private static async Task<JsonElement[]> StageThreeAsync(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition position, object values)
    {
        var result = new JsonElement[3];
        for (var index = 0; index < result.Length; index++) result[index] = await StageAsync(fixture, client, position, values, [(byte)index, (byte)(index + 1), (byte)(index + 2)], $"set-{index}.pdf");
        return result;
    }
    private static async Task<JsonElement[]> StageThroughFilesAsync(BusinessAuditHostFixture fixture, int count)
    {
        using var scope = fixture.Services.CreateScope(); var accessor = fixture.Services.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, fixture.ActorId)], "ActualFixtureActor")), RequestServices = scope.ServiceProvider };
        try
        {
            var files = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>(); var staged = new JsonElement[count];
            for (var index = 0; index < count; index++)
            {
                using var stream = new MemoryStream([(byte)index, (byte)(index + 1), (byte)(index + 2)]);
                staged[index] = JsonSerializer.SerializeToElement(await files.StageAsync(stream, $"maximum-{index}.pdf", "application/pdf", DocumentFileOwnerPolicy.Key), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            return staged;
        }
        finally { accessor.HttpContext = previous; }
    }
    private static async Task<JsonElement> ReadListAsync(BusinessAuditHostFixture fixture, HttpClient client, string code)
    {
        var document = Assert.Single(await RowsAsync(fixture, "Document"), row => Equals(row["ECODE"], code));
        return await FeatureHttpTestRequests.ReadAsync(client, Point(fixture, "document.document.read"), $"/api/document/documents/{document["EID"]}/files");
    }
    private static async Task<Dictionary<string, object?>[]> RowsAsync(BusinessAuditHostFixture fixture, string model)
    {
        using var scope = fixture.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(model).AsNoTracking().ToArrayAsync();
    }
    private static async Task ChangeAsync(BusinessAuditHostFixture fixture, string model, string keyField, string key, string field, object? value)
    {
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var row = await db.Set<Dictionary<string, object?>>(model).SingleAsync(row => EF.Property<string>(row, keyField) == key); row[field] = value; await db.SaveChangesAsync();
    }
    private static async Task AssertUnconsumedAsync(BusinessAuditHostFixture fixture, int count)
    {
        foreach (var model in new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileUsage", "WorkspaceFolderContent" }) Assert.Empty(await RowsAsync(fixture, model));
        var leases = await RowsAsync(fixture, "FileUploadLease"); Assert.Equal(count, leases.Length); Assert.All(leases, row => Assert.Equal("staged", row["State"]));
        var files = await RowsAsync(fixture, "FileRecord"); Assert.Equal(count, files.Length);
        foreach (var file in files)
        {
            Assert.Equal("ready", file["State"]); var path = Path.Combine(fixture.StorageRoot, (string)file["StorageKey"]!);
            var bytes = await File.ReadAllBytesAsync(path); Assert.Equal(file["Length"], (long)bytes.Length); Assert.Equal(file["Sha256"], Convert.ToHexString(SHA256.HashData(bytes)));
        }
    }
    private static async Task AssertActiveCollectionsAsync(BusinessAuditHostFixture fixture, int count)
    {
        Assert.Equal(count, (await RowsAsync(fixture, "DocumentFileLink")).Length);
        var usages = await RowsAsync(fixture, "FileUsage"); Assert.Equal(count, usages.Length); Assert.All(usages, row => Assert.Equal("active", row["State"]));
        foreach (var file in await RowsAsync(fixture, "FileRecord")) { Assert.Equal("ready", file["State"]); Assert.True(File.Exists(Path.Combine(fixture.StorageRoot, (string)file["StorageKey"]!))); }
        Assert.Equal(0, await CleanupAsync(fixture));
    }
    private static async Task<int> CleanupAsync(BusinessAuditHostFixture fixture)
    { using var scope = fixture.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync(); }
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(code, json.GetProperty("code").GetString());
    }
    private static async Task InjectedFailureAsync(Func<Task<HttpResponseMessage>> send)
    {
        try { using var response = await send(); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); }
        catch (Exception exception) when (exception is not Xunit.Sdk.XunitException) { Assert.Contains(nameof(InjectedCollectionFailure), exception.ToString(), StringComparison.Ordinal); }
    }
    private static async Task AssertReadAndDownloadCorruptedAsync(BusinessAuditHostFixture fixture, HttpClient client, string documentId, string linkId)
    {
        foreach (var (behavior, url) in new[] { ("document.document.read", $"/api/document/documents/{documentId}/files"), ("document.document.download", $"/api/document/documents/{documentId}/files/{linkId}/content") })
        {
            try { using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, behavior), HttpMethod.Get, url); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); }
            catch (Exception exception) when (exception is not Xunit.Sdk.XunitException) { Assert.Contains("Corrupted", exception.ToString(), StringComparison.Ordinal); }
        }
    }

    private sealed class InjectedCollectionFailure : Exception { }
    private sealed class CollectionSqlFault : DbCommandInterceptor
    {
        private int _failLock; private int _failDelete; private int _deletes;
        public bool FailReference { get; set; }
        public bool ObserveFileLocks { get; private set; }
        public List<string> FileLocks { get; } = [];
        public int ObservedActiveUsages { get; private set; }
        public int ObservedReleasedUsages { get; private set; }
        public int ObservedAddedLinks { get; private set; }
        public bool ObserveMetadataReads { get; set; }
        public bool ObserveStagedReads { get; set; }
        public List<int> StagedLeaseBatchSizes { get; } = [];
        public List<int> StagedFileBatchSizes { get; } = [];
        public int MetadataCommands { get; private set; }
        public List<int> UsageReadBatchSizes { get; } = [];
        public int RecordReads { get; private set; }
        public int ConfirmedLeaseReads { get; private set; }
        public void ArmLockFailure(int ordinal) { _failLock = ordinal; ObserveLocks(); }
        public void ObserveLocks() { FileLocks.Clear(); ObserveFileLocks = true; }
        public void ArmDeleteFailure(int ordinal) { _deletes = 0; _failDelete = ordinal; ObserveFileLocks = false; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Observe(command, eventData); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Observe(command, eventData); return ValueTask.FromResult(result); }
        private void Observe(DbCommand command, CommandEventData data)
        {
            // 首次业务插入结束预读观察窗口，避免把 Confirm 的锁内重读计为集合批读。
            if (ObserveStagedReads && command.CommandText.Contains("[Document]", StringComparison.Ordinal)
                && (command.CommandText.Contains("INSERT", StringComparison.Ordinal) || command.CommandText.Contains("MERGE", StringComparison.Ordinal)))
                ObserveStagedReads = false;
            if (ObserveStagedReads)
            {
                var fragments = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<string>().Prepend(command.CommandText);
                var count = fragments.SelectMany(fragment => Regex.Matches(fragment, "File(?:UploadLease|Record)_[0-9a-f]{32}").Select(match => match.Value)).Distinct(StringComparer.Ordinal).Count();
                if (command.CommandText.Contains("[FileUploadLease]", StringComparison.Ordinal)) StagedLeaseBatchSizes.Add(count);
                if (command.CommandText.Contains("[FileRecord]", StringComparison.Ordinal)) StagedFileBatchSizes.Add(count);
            }
            if (ObserveMetadataReads)
            {
                MetadataCommands++;
                if (command.CommandText.Contains("[FileUsage]", StringComparison.Ordinal))
                {
                    var fragments = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<string>().Prepend(command.CommandText);
                    UsageReadBatchSizes.Add(fragments.SelectMany(fragment => Regex.Matches(fragment, "FileUsage_[0-9a-f]{32}").Select(match => match.Value)).Distinct(StringComparer.Ordinal).Count());
                }
                if (command.CommandText.Contains("[FileRecord]", StringComparison.Ordinal)) RecordReads++;
                if (command.CommandText.Contains("[FileUploadLease]", StringComparison.Ordinal) && command.CommandText.Contains("[ConfirmedUsageId]", StringComparison.Ordinal)) ConfirmedLeaseReads++;
            }
            var fileLock = command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)
                ? command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<string>().SingleOrDefault(value => value.StartsWith("Files.", StringComparison.Ordinal)) : null;
            if (fileLock is not null && ObserveFileLocks) FileLocks.Add(fileLock);
            var failLock = fileLock is not null && _failLock > 0 && FileLocks.Count == _failLock;
            var failReference = FailReference && command.CommandText.Contains("[WorkspaceFolderContent]", StringComparison.Ordinal)
                && (command.CommandText.Contains("INSERT", StringComparison.Ordinal) || command.CommandText.Contains("MERGE", StringComparison.Ordinal));
            var failDelete = _failDelete > 0 && command.CommandText.Contains("[Document]", StringComparison.Ordinal) && command.CommandText.Contains("DELETE", StringComparison.Ordinal) && ++_deletes == _failDelete;
            if (!failLock && !failReference && !failDelete) return;
            var entries = data.Context!.ChangeTracker.Entries().ToArray();
            ObservedActiveUsages = entries.Count(entry => entry.Metadata.Name == "FileUsage" && Equals(entry.Property("State").CurrentValue, "active"));
            ObservedReleasedUsages = entries.Count(entry => entry.Metadata.Name == "FileUsage" && Equals(entry.Property("State").CurrentValue, "released"));
            ObservedAddedLinks = entries.Count(entry => entry.Metadata.Name == "DocumentFileLink" && entry.State == EntityState.Added);
            _failLock = 0; _failDelete = 0; FailReference = false; throw new InjectedCollectionFailure();
        }
    }

    private sealed class CollectionCommitControl : DbTransactionInterceptor
    {
        private Guid _target;
        public bool Armed { get; set; }
        public bool Pause { get; init; }
        public bool ThrowAfterCommit { get; init; }
        public int ObservedLinks { get; private set; }
        public int ObservedUsages { get; private set; }
        public int Injected { get; private set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RolledBack { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries().ToArray();
            if (!Armed || !entries.Any(entry => entry.Metadata.Name == "DocumentFileLink")) return result;
            Armed = false; _target = eventData.TransactionId;
            ObservedLinks = entries.Count(entry => entry.Metadata.Name == "DocumentFileLink"); ObservedUsages = entries.Count(entry => entry.Metadata.Name == "FileUsage");
            Assert.Equal(3, ObservedLinks); Assert.Equal(3, ObservedUsages);
            if (Pause) { Reached.TrySetResult(); await Resume.Task.WaitAsync(cancellationToken); throw new InjectedCollectionFailure(); }
            return result;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (ThrowAfterCommit && eventData.TransactionId == _target) { Injected++; throw new InjectedCollectionFailure(); } return Task.CompletedTask; }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (eventData.TransactionId == _target) RolledBack.TrySetResult(); return Task.CompletedTask; }
    }

    private sealed class CompleteCollectionSave : SaveChangesInterceptor
    {
        public bool ObservedCompleteAddedCollection { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var links = eventData.Context!.ChangeTracker.Entries().Where(entry => entry.Metadata.Name == "DocumentFileLink" && entry.State == EntityState.Added).ToArray();
            if (links.Length > 0)
            {
                Assert.Equal(3, links.Length);
                Assert.All(links, entry => Assert.IsType<string>(entry.Property("FileUsageId").CurrentValue));
                Assert.Equal(3, eventData.Context.ChangeTracker.Entries().Count(entry => entry.Metadata.Name == "FileUsage" && entry.State == EntityState.Added));
                ObservedCompleteAddedCollection = true;
            }
            return ValueTask.FromResult(result);
        }
    }
}
