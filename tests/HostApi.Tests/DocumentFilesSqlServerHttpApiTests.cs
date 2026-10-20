using System.Net;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>真实 Document 首个使用者贯通正规上传、原子保存、所属安全元数据与授权开流。</summary>
public sealed partial class DocumentFilesSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "单main真实Stage到Document使用、目录关系、Metadata和Open贯通")]
    public async Task MainFile_StagesCreatesReadsDownloadsAndOwnDeleteReleases()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        byte[] bytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x0A, 0x00, 0xFF];
        var values = new { ECODE = "MAIN-01", ENAME = "主文件图文档", Description = "真实流" };
        var staged = await StageAsync(fixture, admin, position, values, bytes);
        string documentId, contentLinkId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal(0, await db.Set<Dictionary<string, object?>>("Document").CountAsync());
            var file = await db.Set<Dictionary<string, object?>>("FileRecord").SingleAsync();
            Assert.Equal("ready", file["State"]);
            Assert.Equal((long)bytes.Length, file["Length"]);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), file["Sha256"]);
        }
        using var create = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Post,
            "/api/workspace/content/documents", new { position, values, uploads = new { mainUploadId = staged.GetProperty("uploadId").GetString() } });
        Assert.True(create.StatusCode == HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var receipt = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.False(receipt.TryGetProperty("id", out _));
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var document = await db.Set<Dictionary<string, object?>>("Document").SingleAsync();
            documentId = (string)document["EID"]!;
            var iteration = await db.Set<Dictionary<string, object?>>("DocumentIteration").SingleAsync();
            var link = await db.Set<Dictionary<string, object?>>("DocumentFileLink").SingleAsync();
            var usage = await db.Set<Dictionary<string, object?>>("FileUsage").SingleAsync();
            var lease = await db.Set<Dictionary<string, object?>>("FileUploadLease").SingleAsync();
            Assert.Equal(iteration["EID"], link["PEID"]);
            Assert.Equal(0L, link["Ordinal"]);
            Assert.Equal("main", link["Role"]);
            Assert.Equal(usage["EID"], link["FileUsageId"]);
            Assert.Equal(link["LID"], usage["OwnerObjectId"]);
            Assert.Equal(link["CEID"], usage["FileId"]);
            Assert.Equal("active", usage["State"]);
            Assert.Equal("confirmed", lease["State"]);
            Assert.Equal(usage["EID"], lease["ConfirmedUsageId"]);
            var ws = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "ChildModelKey") == "Document");
            Assert.Equal(documentId, ws["CEID"]);
            contentLinkId = (string)ws["LID"]!;
        }
        var list = await DocumentFileHttpTestRequests.ReadAsync(fixture, admin, Point(fixture, "document.document.read"), $"/api/document/documents/{documentId}/files");
        var item = Assert.Single(list.GetProperty("files").EnumerateArray());
        Assert.True(list.GetProperty("downloadAllowed").GetBoolean());
        Assert.Equal("main.pdf", item.GetProperty("fileName").GetString());
        foreach (var forbidden in new[] { "fileId", "usageId", "storageKey", "storagePath" }) Assert.False(item.TryGetProperty(forbidden, out _));
        using var download = await DocumentFileHttpTestRequests.SendAsync(fixture, admin, Point(fixture, "document.document.download"), HttpMethod.Get,
            $"/api/document/documents/{documentId}/files/{item.GetProperty("fileLinkId").GetString()}/content");
        Assert.True(download.StatusCode == HttpStatusCode.OK, await download.Content.ReadAsStringAsync());
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(bytes.Length, download.Content.Headers.ContentLength);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        using var deleted = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + contentLinkId, new { position });
        Assert.True(deleted.StatusCode == HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());
        using var finalScope = fixture.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        foreach (var model in new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "WorkspaceFolderContent" })
            Assert.Equal(model == "WorkspaceFolderContent" ? 1 : 0, await finalDb.Set<Dictionary<string, object?>>(model).CountAsync());
        Assert.Equal("released", (await finalDb.Set<Dictionary<string, object?>>("FileUsage").SingleAsync())["State"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "严格Document DTO与非法集合不消费真实暂存，空附件与metadata-only保持兼容")]
    public async Task CreateInput_RejectsMalformedAndInvalidSetsWithoutConsumingMain()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var values = new { ECODE = "STRICT-01", ENAME = "严格上传集合" };
        var staged = await StageAsync(fixture, admin, position, values, [1, 2, 3]);
        var uploadId = staged.GetProperty("uploadId").GetString()!;
        var uploadSets = new object[]
        {
            new { mainUploadId = "" }, new { mainUploadId = (string?)null }, new { mainUploadId = " " + uploadId },
            new { mainUploadId = uploadId.ToUpperInvariant() }, new { mainUploadId = uploadId + "\n" },
            new { mainUploadId = staged.GetProperty("fileId").GetString() },
            new { mainUploadId = uploadId, attachmentUploadIds = new[] { uploadId } },
            new { mainUploadId = uploadId, attachmentUploadIds = Enumerable.Repeat(uploadId, 101).ToArray() }
        };
        foreach (var uploads in uploadSets)
        {
            using var response = await CreateDocumentAsync(fixture, admin, new { position, values, uploads });
            var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "Document.InvalidUploadSet");
            Assert.Contains(problem.GetProperty("fieldErrors").EnumerateArray(), field => field.GetProperty("field").GetString() == "uploads");
        }
        using (var unavailable = await CreateDocumentAsync(fixture, admin, new { position, values,
            uploads = new { mainUploadId = uploadId, attachmentUploadIds = new[] { "FileUploadLease_0123456789abcdef0123456789abcdef" } } }))
            await ProblemAsync(unavailable, HttpStatusCode.NotFound, "Files.UploadUnavailable");
        var malformed = new object[]
        {
            new { position, values, uploads = new { attachmentUploadIds = Array.Empty<string>() } },
            new { position, values, uploads = new { mainUploadId = uploadId, actorId = "forged" } },
            new { position, values, uploads = new[] { uploadId } },
            new { position, values, uploads = new { mainUploadId = uploadId, attachmentUploadIds = "other" } },
            new { position, values, ownerKey = "other" }, new { position, uploads = new { mainUploadId = uploadId } },
            new { values, uploads = new { mainUploadId = uploadId } },
            new { position = new { position.SpaceId, position.RootFolderId, position.LinkIds, parentId = "forged" }, values },
            new { position = new { position.SpaceId, position.RootFolderId }, values }
        };
        foreach (var input in malformed)
        {
            using var response = await CreateDocumentAsync(fixture, admin, input);
            await ProblemAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        }
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal("staged", (await db.Set<Dictionary<string, object?>>("FileUploadLease").SingleAsync())["State"]);
            foreach (var model in new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileUsage", "WorkspaceFolderContent" })
                Assert.Equal(model == "WorkspaceFolderContent" ? 1 : 0, await db.Set<Dictionary<string, object?>>(model).CountAsync());
        }
        // 集合已开放，空数组、显式null及省略附件都仍表示仅主文件；每次使用独立合法暂存。
        foreach (var variant in new[] { "empty", "null", "omitted" })
        {
            var acceptedValues = new { ECODE = variant == "empty" ? values.ECODE : "STRICT-" + variant.ToUpperInvariant(), ENAME = values.ENAME };
            var acceptedUploadId = variant == "empty" ? uploadId
                : (await StageAsync(fixture, admin, position, acceptedValues, [1, 2, 3])).GetProperty("uploadId").GetString()!;
            object acceptedUploads = variant switch
            {
                "empty" => new { mainUploadId = acceptedUploadId, attachmentUploadIds = Array.Empty<string>() },
                "null" => new { mainUploadId = acceptedUploadId, attachmentUploadIds = (string[]?)null },
                _ => new { mainUploadId = acceptedUploadId }
            };
            using var accepted = await CreateDocumentAsync(fixture, admin, new { position, values = acceptedValues, uploads = acceptedUploads });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var acceptedFacts = await ReadCreatedAsync(fixture, acceptedValues.ECODE);
            var acceptedFiles = await DocumentFileHttpTestRequests.ReadAsync(fixture, admin, Point(fixture, "document.document.read"), $"/api/document/documents/{acceptedFacts.DocumentId}/files");
            var main = Assert.Single(acceptedFiles.GetProperty("files").EnumerateArray());
            Assert.Equal("main", main.GetProperty("role").GetString());
            Assert.Equal(0L, main.GetProperty("ordinal").GetInt64());
        }
        using var legacy = await CreateDocumentAsync(fixture, admin, new { position, values = new { ECODE = "LEGACY-01", ENAME = "无文件创建" }, uploads = (object?)null });
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        var facts = await ReadCreatedAsync(fixture, "LEGACY-01");
        var files = await DocumentFileHttpTestRequests.ReadAsync(fixture, admin, Point(fixture, "document.document.read"), $"/api/document/documents/{facts.DocumentId}/files");
        Assert.Empty(files.GetProperty("files").EnumerateArray());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "手工multipart顺序、JSON字节预算与文件准入在真实HTTP/SQL边界拒绝")]
    public async Task MultipartInput_EnforcesByteBudgetsPartOrderAndOwnedValidation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var positionJson = JsonSerializer.Serialize(position);
        var valuesJson = JsonSerializer.Serialize(new { ECODE = "MULTIPART-01", ENAME = "完整准入" });
        var badParts = new[]
        {
            new[] { ("values", valuesJson), ("position", positionJson) },
            new[] { ("position", positionJson), ("position", positionJson) },
            new[] { ("position", positionJson), ("unknown", valuesJson) },
            new[] { ("position", "{"), ("values", valuesJson) },
            new[] { ("position", JsonSerializer.Serialize(new { position.SpaceId, position.RootFolderId, position.LinkIds, other = 1 })), ("values", valuesJson) },
            new[] { ("position", positionJson), ("values", valuesJson + new string(' ', 65537)) }
        };
        for (var index = 0; index < badParts.Length; index++)
        {
            using var request = MultipartRequest(fixture, badParts[index], [1, 2], "main.pdf");
            using var response = await admin.SendAsync(request);
            await ProblemAsync(response, index == badParts.Length - 1 ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.BadRequest,
                index == badParts.Length - 1 ? "Files.UploadTooLarge" : "REQUEST_VALIDATION_FAILED");
        }
        foreach (var (values, code) in new (object, string)[]
        {
            (new { ENAME = "缺代码" }, "Document.CodeRequired"),
            (new { ECODE = "EMPTY-NAME", ENAME = "" }, "Document.NameRequired"),
            (new { ECODE = "LONG-DESC", ENAME = "超长说明", Description = new string('x', 501) }, "Document.DescriptionTooLong"),
            (new { ECODE = "FORGED", ENAME = "服务端字段", FileUsageId = "forged" }, "Workspace.ServerFieldForbidden")
        })
        {
            using var request = UploadRequest(fixture, position, values, new ByteArrayContent([1]), "main.pdf");
            using var response = await admin.SendAsync(request);
            await ProblemAsync(response, HttpStatusCode.BadRequest, code);
        }
        using (var scope = fixture.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("FileRecord").CountAsync());
        foreach (var (bytes, name, code) in new (byte[], string, string)[]
        {
            ([], "main.pdf", "Files.EmptyContent"), ([1], "main.exe", "Document.ExtensionNotAllowed"),
            ([1], new string('n', 253) + ".pdf", "Files.InvalidFileName")
        })
        {
            using var request = UploadRequest(fixture, position, new { ECODE = "FILE-01", ENAME = "文件准入" }, new ByteArrayContent(bytes), name);
            using var response = await admin.SendAsync(request);
            await ProblemAsync(response, HttpStatusCode.BadRequest, code);
        }
        var remaining = 65536 - Encoding.UTF8.GetByteCount(positionJson) - Encoding.UTF8.GetByteCount(valuesJson);
        using var exact = MultipartRequest(fixture, [("position", positionJson), ("values", valuesJson + new string(' ', remaining))], [1, 2, 3], "MAIN.PDF");
        using var allowed = await admin.SendAsync(exact);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var extra = MultipartRequest(fixture, [("position", positionJson), ("values", valuesJson)], [1, 2, 3], "extra.pdf", extra: true);
        using var rejected = await admin.SendAsync(extra);
        await ProblemAsync(rejected, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        using var finalScope = fixture.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Equal(0, await finalDb.Set<Dictionary<string, object?>>("Document").CountAsync());
        Assert.Single(await finalDb.Set<Dictionary<string, object?>>("FileUploadLease").Where(row => EF.Property<string>(row, "State") == "staged").ToArrayAsync());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "合法multipart的所有文件超限均由Files返回413，已知长度与非seek请求一致")]
    public async Task MultipartInput_FileLimitProduces413ForKnownAndNonSeekableBodies()
    {
        const int maximum = 1024;
        await using var fixture = await SmallUploadFixtureAsync(maximum);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var outcomes = new List<(bool UnknownLength, int Length, HttpStatusCode Status, string? Code)>();
        foreach (var unknownLength in new[] { false, true })
        foreach (var length in new[] { maximum, maximum + 1, maximum + 2, maximum * 2 })
        {
            using HttpContent content = unknownLength
                ? new StreamContent(new ForwardOnlyUploadStream(new byte[length]))
                : new ByteArrayContent(new byte[length]);
            using var request = UploadRequest(fixture, position, new { ECODE = $"LIMIT-{unknownLength}-{length}", ENAME = "单文件字节边界" }, content, "main.pdf");
            Assert.Equal(!unknownLength, request.Content!.Headers.ContentLength.HasValue);
            using var response = await admin.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            outcomes.Add((unknownLength, length, response.StatusCode, body.TryGetProperty("code", out var code) ? code.GetString() : null));
        }
        // 一轮保留全部边界信号，避免+2的首个失败掩盖未知长度分支。
        Assert.All(outcomes, outcome =>
        {
            var expected = outcome.Length == maximum ? HttpStatusCode.OK : HttpStatusCode.RequestEntityTooLarge;
            Assert.True(outcome.Status == expected, $"unknownLength={outcome.UnknownLength}, length={outcome.Length}: expected {expected}, actual {outcome.Status}/{outcome.Code}");
            if (outcome.Length > maximum) Assert.Equal("Files.UploadTooLarge", outcome.Code);
        });
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var files = await db.Set<Dictionary<string, object?>>("FileRecord").ToArrayAsync();
        Assert.Equal(8, files.Length);
        var ready = files.Where(row => Equals(row["State"], "ready")).ToArray();
        Assert.Equal(2, ready.Length);
        Assert.All(ready, row => Assert.Equal((long)maximum, row["Length"]));
        Assert.Equal(2, await db.Set<Dictionary<string, object?>>("FileUploadLease").CountAsync(row => EF.Property<string>(row, "State") == "staged"));
        Assert.Empty(await db.Set<Dictionary<string, object?>>("FileUsage").ToArrayAsync());
        Assert.Empty(await db.Set<Dictionary<string, object?>>("Document").ToArrayAsync());
        Assert.Equal(2, Directory.EnumerateFiles(fixture.StorageRoot).Count(path => !path.EndsWith(".part", StringComparison.Ordinal)));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "小文件限额不取代独立JSON预算，非法framing和header仍400，总请求超限仍413")]
    public async Task MultipartInput_SmallFileLimitPreservesJsonFramingAndEnvelopeBudgets()
    {
        const int maximum = 1024;
        await using var fixture = await SmallUploadFixtureAsync(maximum);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var positionJson = JsonSerializer.Serialize(position);
        var valuesJson = JsonSerializer.Serialize(new { ECODE = "SMALL-LIMIT", ENAME = "独立元数据预算" });
        using (var request = MultipartRequest(fixture, [("position", positionJson), ("values", valuesJson + new string(' ', 2048))], [1], "main.pdf"))
        using (var response = await admin.SendAsync(request))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var request = MultipartRequest(fixture, [("position", positionJson), ("values", valuesJson + new string(' ', 65537))], [1], "main.pdf"))
        using (var response = await admin.SendAsync(request))
            await ProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "Files.UploadTooLarge");
        foreach (var oversizedHeader in new[] { false, true })
        {
            using var content = new ByteArrayContent([1]);
            if (oversizedHeader) content.Headers.Add("X-Upload-Test", new string('x', 65537));
            else for (var index = 0; index < 17; index++) content.Headers.Add($"X-Upload-Test-{index}", "x");
            using var request = UploadRequest(fixture, position, new { ECODE = "HEADER-LIMIT", ENAME = "头预算" }, content, "main.pdf");
            using var response = await admin.SendAsync(request);
            await ProblemAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        }
        const string boundary = "document-limit-boundary";
        var prefix = $"--{boundary}\r\nContent-Disposition: form-data; name=\"position\"\r\n\r\n{positionJson}\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"values\"\r\n\r\n{valuesJson}\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"main.pdf\"\r\nContent-Type: application/pdf\r\n\r\nx";
        using (var request = RawMultipartRequest(fixture, boundary, new ByteArrayContent(Encoding.UTF8.GetBytes(prefix))))
        using (var response = await admin.SendAsync(request))
            await ProblemAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        var envelope = Encoding.UTF8.GetBytes(prefix + $"\r\n--{boundary}--\r\n" + new string(' ', maximum + 131073));
        foreach (var unknownLength in new[] { false, true })
        {
            using HttpContent content = unknownLength
                ? new StreamContent(new ForwardOnlyUploadStream(envelope)) : new ByteArrayContent(envelope);
            using var request = RawMultipartRequest(fixture, boundary, content);
            Assert.Equal(!unknownLength, request.Content!.Headers.ContentLength.HasValue);
            using var response = await admin.SendAsync(request);
            await ProblemAsync(response, unknownLength ? HttpStatusCode.BadRequest : HttpStatusCode.RequestEntityTooLarge,
                unknownLength ? "REQUEST_VALIDATION_FAILED" : "Files.UploadTooLarge");
        }
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Single(await db.Set<Dictionary<string, object?>>("FileUploadLease").Where(row => EF.Property<string>(row, "State") == "staged").ToArrayAsync());
        Assert.Single(await db.Set<Dictionary<string, object?>>("FileRecord").Where(row => EF.Property<string>(row, "State") == "ready").ToArrayAsync());
        Assert.Empty(await db.Set<Dictionary<string, object?>>("FileUsage").ToArrayAsync());
        Assert.Empty(await db.Set<Dictionary<string, object?>>("Document").ToArrayAsync());
        // 真实框架 HTTP 拒绝不是普通输入 IOException；覆盖 section header 和 section body 两个读取边界。
        var raw = Encoding.UTF8.GetBytes(prefix + $"\r\n--{boundary}--\r\n");
        var bodyOffset = Encoding.UTF8.GetByteCount($"--{boundary}\r\nContent-Disposition: form-data; name=\"position\"\r\n\r\n") + 1;
        foreach (var rejectAt in new[] { 0, bodyOffset })
        {
            using var input = new ForwardOnlyUploadStream(raw, rejectAt);
            var context = new DefaultHttpContext();
            context.Request.ContentType = $"multipart/form-data; boundary={boundary}";
            context.Request.Body = input;
            var controller = new WorkspaceDocumentUploadsController(scope.ServiceProvider.GetRequiredService<WorkspaceContents>(),
                scope.ServiceProvider.GetRequiredService<IFileLifecycleService>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceDocumentUploadsController>.Instance)
            { ControllerContext = new() { HttpContext = context } };
            var failure = await Assert.ThrowsAsync<BadHttpRequestException>(() => controller.Stage(CancellationToken.None));
            Assert.Same(input.Rejection, failure);
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, failure.StatusCode);
            Assert.Contains(rejectAt == 0 ? "ReadSectionAsync" : "MultipartInputStream.ReadAsync", failure.StackTrace);
        }
    }

    private static Task<BusinessAuditHostFixture> SmallUploadFixtureAsync(long maximum) => BusinessAuditHostFixture.CreateAsync(services =>
    {
        services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(FileCleanupWorker)));
        services.PostConfigure<FilesOptions>(options => options.MaxUploadSizeBytes = maximum);
    }, includeWorkspace: true);

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "截断JSON、header及file仅翻译输入解析IO，真实存储写IO继续500")]
    public async Task MultipartInput_TruncatedSectionsAre400AndStorageIoRemains500()
    {
        StorageWriteFailure? storage = null;
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(FileCleanupWorker)));
            services.AddSingleton<IFileContentStore>(provider => storage = new StorageWriteFailure(provider.GetRequiredService<LocalStorageProvider>(),
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FilesOptions>>().Value.StorageRoot));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var positionJson = JsonSerializer.Serialize(position);
        var values = new { ECODE = "TRUNCATED", ENAME = "输入解析边界" };
        var valuesJson = JsonSerializer.Serialize(values);
        const string boundary = "truncated-input-boundary";
        var positionHeader = $"--{boundary}\r\nContent-Disposition: form-data; name=\"position\"\r\n";
        var positionPart = positionHeader + $"\r\n{positionJson}\r\n";
        var valuesHeader = $"--{boundary}\r\nContent-Disposition: form-data; name=\"values\"\r\n\r\n";
        var outcomes = new List<(string Section, HttpStatusCode Status, string? Code)>();
        foreach (var (section, raw) in new[]
        {
            ("header", positionHeader), ("position", positionHeader + $"\r\n{positionJson}"),
            ("values", positionPart + valuesHeader + valuesJson),
            ("file", positionPart + valuesHeader + valuesJson + $"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"main.pdf\"\r\n\r\nx")
        })
        {
            using var request = RawMultipartRequest(fixture, boundary, new ByteArrayContent(Encoding.UTF8.GetBytes(raw)));
            using var response = await admin.SendAsync(request);
            var body = response.StatusCode == HttpStatusCode.BadRequest ? await response.Content.ReadFromJsonAsync<JsonElement>() : default;
            outcomes.Add((section, response.StatusCode, body.ValueKind == JsonValueKind.Object && body.TryGetProperty("code", out var code) ? code.GetString() : null));
        }
        // 同一真实Local先完成part写入，再以CreateNew碰撞制造存储IOException；它不属于输入解析错误。
        using (var request = UploadRequest(fixture, position, values, new ByteArrayContent([1, 2, 3]), "main.pdf"))
        using (var response = await admin.SendAsync(request))
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, storage!.WriteFailures);
        Assert.All(outcomes, outcome =>
        {
            Assert.True(outcome.Status == HttpStatusCode.BadRequest, $"{outcome.Section}: expected BadRequest, actual {outcome.Status}");
            Assert.Equal("REQUEST_VALIDATION_FAILED", outcome.Code);
        });
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Empty(await db.Set<Dictionary<string, object?>>("FileRecord").Where(row => EF.Property<string>(row, "State") == "ready").ToArrayAsync());
        Assert.Empty(await db.Set<Dictionary<string, object?>>("FileUploadLease").Where(row => EF.Property<string>(row, "State") == "staged").ToArrayAsync());
        Assert.Empty(await db.Set<Dictionary<string, object?>>("FileUsage").ToArrayAsync());
        Assert.Empty(await db.Set<Dictionary<string, object?>>("Document").ToArrayAsync());
    }

    private sealed class StorageWriteFailure(LocalStorageProvider inner, string root) : IFileContentStore
    {
        public int WriteFailures { get; private set; }
        public async Task<FileContentMeasurement> WritePartialAsync(string key, Stream source, long maximumBytes, CancellationToken token)
        {
            var measurement = await inner.WritePartialAsync(key, source, maximumBytes, token);
            try { using var collision = new FileStream(Path.Combine(root, key + ".part"), FileMode.CreateNew, FileAccess.Write); }
            catch (IOException) { WriteFailures++; throw; }
            return measurement;
        }
        public Task CompleteAsync(string key, CancellationToken token) => inner.CompleteAsync(key, token);
        public Task<Stream> OpenAsync(string key, CancellationToken token) => inner.OpenAsync(key, token);
        public Task DeleteContentAsync(string key, CancellationToken token) => inner.DeleteContentAsync(key, token);
    }

    private static HttpRequestMessage RawMultipartRequest(BusinessAuditHostFixture fixture, string boundary, HttpContent content)
    {
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads") { Content = content };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
        return request;
    }

    private sealed class ForwardOnlyUploadStream(byte[] bytes, int? rejectAt = null) : Stream
    {
        private readonly MemoryStream _source = new(bytes, writable: false);
        public BadHttpRequestException? Rejection { get; } = rejectAt.HasValue ? new("Request body limit probe.", StatusCodes.Status413PayloadTooLarge) : null;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _source.Read(buffer, offset, ReadCount(count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _source.ReadAsync(buffer[..ReadCount(buffer.Length)], token);
        private int ReadCount(int requested)
        {
            if (!rejectAt.HasValue) return requested;
            var remaining = rejectAt.Value - (int)_source.Position;
            if (remaining <= 0) throw Rejection!;
            return Math.Min(requested, remaining);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实暂存的主体、用途、到期、取消与消费状态不能被保存入口绕过")]
    public async Task MainLease_RejectsOtherActorPurposeAndUnavailableStatesWithoutConsumption()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var actor = await ActorAsync(fixture, admin, "LeaseWriter");
        using var ordinary = actor.Client;
        var values = new { ECODE = "LEASE-01", ENAME = "暂存身份边界" };
        var owned = await StageAsync(fixture, admin, position, values, [1, 2, 3]);
        var ownedId = owned.GetProperty("uploadId").GetString()!;
        using (var stolen = await SaveMainAsync(fixture, ordinary, position, values, ownedId))
            await ProblemAsync(stolen, HttpStatusCode.NotFound, "Files.UploadUnavailable");
        using (var foreignCancel = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Delete,
            "/api/workspace/content/document-uploads/" + ownedId))
            await ProblemAsync(foreignCancel, HttpStatusCode.NotFound, "Files.UploadUnavailable");
        Assert.Equal("staged", Assert.Single((await ObserveBusinessAsync(fixture)).Leases)["State"]);
        using (var unknown = await SaveMainAsync(fixture, admin, position, values, "FileUploadLease_00000000000000000000000000000000"))
            await ProblemAsync(unknown, HttpStatusCode.NotFound, "Files.UploadUnavailable");
        await ChangeRowAsync(fixture, "FileUploadLease", "EID", ownedId, "UsageOwnerKey", "another.registered.purpose");
        using (var wrongPurpose = await SaveMainAsync(fixture, admin, position, values, ownedId))
            await ProblemAsync(wrongPurpose, HttpStatusCode.NotFound, "Files.UploadUnavailable");
        await ChangeRowAsync(fixture, "FileUploadLease", "EID", ownedId, "UsageOwnerKey", "document.fileLink");
        await ChangeRowAsync(fixture, "FileUploadLease", "EID", ownedId, "ExpiresAt", DateTime.UtcNow.AddMinutes(-1));
        using (var expired = await SaveMainAsync(fixture, admin, position, values, ownedId))
            await ProblemAsync(expired, HttpStatusCode.Conflict, "Files.UploadExpired");
        var cancelled = await StageAsync(fixture, admin, position, values, [4, 5]);
        var cancelledId = cancelled.GetProperty("uploadId").GetString()!;
        using (var cancel = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Delete,
            "/api/workspace/content/document-uploads/" + cancelledId)) Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        using (var rejected = await SaveMainAsync(fixture, admin, position, values, cancelledId))
            await ProblemAsync(rejected, HttpStatusCode.Conflict, "Files.UploadCancelled");
        Assert.Empty((await ObserveBusinessAsync(fixture)).Documents);
        Assert.Empty((await ObserveBusinessAsync(fixture)).Usages);
        var accepted = await CreateMainAsync(fixture, admin, position, "LEASE-CONSUMED", [6, 7]);
        using (var consumed = await SaveMainAsync(fixture, admin, position, values, accepted.UploadId))
            await ProblemAsync(consumed, HttpStatusCode.Conflict, "Files.UploadAlreadyConfirmed");
        var final = await ObserveBusinessAsync(fixture);
        Assert.Single(final.Documents);
        Assert.Equal("confirmed", Assert.Single(final.Leases, row => Equals(row["EID"], accepted.UploadId))["State"]);
        Assert.Equal("active", Assert.Single(final.Usages)["State"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "完整位置准入在Stage先验及保存时重验，撤权和移除路径不消费合法暂存")]
    public async Task PositionAndCandidate_RejectBeforeIoAndRecheckAfterStageThenAllowCorrection()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await RootAsync(fixture, admin);
        var folder = await CreateFolderPositionAsync(fixture, admin, root, "暂存出现位置");
        var actor = await ActorAsync(fixture, admin, "PositionWriter");
        using var ordinary = actor.Client;
        var values = new { ECODE = "POSITION-01", ENAME = "准入候选" };
        var staged = await StageAsync(fixture, ordinary, folder.Position, values, [1, 2, 3]);
        var uploadId = staged.GetProperty("uploadId").GetString()!;
        foreach (var (resource, field, value, action, status) in new[]
        {
            ("workspace.folder", "EID", root.RootFolderId, "read", HttpStatusCode.NotFound),
            ("workspace.folder", "EID", folder.FolderId, "edit", HttpStatusCode.Forbidden),
            ("document.document", "ENAME", values.ENAME, "create", HttpStatusCode.Forbidden)
        })
        {
            var ruleId = "ChangedAdmission-" + action;
            await SaveRuleAsync(fixture, admin, ruleId, resource, field, value, action);
            using (var request = UploadRequest(fixture, folder.Position, values, new ByteArrayContent([9]), "main.pdf"))
            {
                request.Headers.Add("X-NewP-Data-Declaration", WorkspaceObjectDeclaration(fixture, "workspace.content.createDocument", "workspace.folder.createDocument").Id);
                using var deniedStage = await ordinary.SendAsync(request);
                Assert.Equal(status, deniedStage.StatusCode);
            }
            using (var deniedSave = await SaveMainAsync(fixture, ordinary, folder.Position, values, uploadId)) Assert.Equal(status, deniedSave.StatusCode);
            var unchanged = await ObserveBusinessAsync(fixture);
            Assert.Single(unchanged.Files);
            Assert.Equal("staged", Assert.Single(unchanged.Leases)["State"]);
            Assert.Empty(unchanged.Documents);
            Assert.Empty(unchanged.Usages);
            await SaveRuleAsync(fixture, admin, ruleId, resource, field, "none", action);
        }
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
            "/api/workspace/content/references/" + folder.Position.LinkIds[0], new { position = root }, WorkspaceClipboardSqlServerHttpApiTests.ClipboardDeclaration(fixture, Point(fixture, "workspace.folder.remove")))) Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var vanished = await SaveMainAsync(fixture, ordinary, folder.Position, values, uploadId)) Assert.Equal(HttpStatusCode.NotFound, vanished.StatusCode);
        Assert.Equal("staged", Assert.Single((await ObserveBusinessAsync(fixture)).Leases)["State"]);
        var correctedFolder = await CreateFolderPositionAsync(fixture, admin, root, "修正保存目录");
        using var corrected = await SaveMainAsync(fixture, ordinary, correctedFolder.Position, new { ECODE = "POSITION-CORRECTED", ENAME = "已修正位置和字段" }, uploadId);
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        Assert.Single((await ObserveBusinessAsync(fixture)).Documents);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实普通主体可读安全列表但download独立拒绝，隐藏和错目标不泄漏，create无read仍成功")]
    public async Task MetadataAndDownload_SeparateDataActionsAndCreateReceiptFromRead()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var first = await CreateMainAsync(fixture, admin, position, "PERMISSION-01", [1, 2, 3]);
        var second = await CreateMainAsync(fixture, admin, position, "PERMISSION-02", [4, 5, 6]);
        var workspaceEntry = fixture.FeatureCatalog.Single(point => point.ModuleId == "Workspace" && point.Key == "workspace");
        var documentRead = Point(fixture, "document.document.read");
        var documentDownload = Point(fixture, "document.document.download");
        Assert.Contains(workspaceEntry.Position, documentRead.EntryDependencies);
        Assert.Contains(workspaceEntry.Position, documentDownload.EntryDependencies);
        Assert.DoesNotContain(fixture.FeatureCatalog, point => point.ParentPosition is null && point.ModuleId is "Part" or "Document");
        var actor = await ActorAsync(fixture, admin, "FileReader");
        using var ordinary = actor.Client;
        using (var allowed = await DocumentFileHttpTestRequests.SendAsync(fixture, ordinary, documentDownload, HttpMethod.Get, ContentUrl(first)))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal(new byte[] { 1, 2, 3 }, await allowed.Content.ReadAsByteArrayAsync());
        }
        using (var wrongOwner = await FeatureHttpTestRequests.SendAsync(ordinary, workspaceEntry, HttpMethod.Get, FilesUrl(first)))
            Assert.Equal(HttpStatusCode.Forbidden, wrongOwner.StatusCode);
        using (var retired = new HttpRequestMessage(HttpMethod.Get, FilesUrl(first)))
        {
            retired.Headers.Add("X-NewP-Feature", "Document/navigation:document.documents");
            using var denied = await ordinary.SendAsync(retired);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var readOnly = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReadOnlyFiles", "只读文件", "readonly-files", "只读文件账号",
            Point(fixture, "document.document.read"));
        using (var reader = readOnly.Client)
        {
            var detail = await DocumentFileHttpTestRequests.ReadAsync(fixture, reader, documentRead, "/api/document/documents/" + first.DocumentId);
            Assert.Equal(first.DocumentId, detail.GetProperty("fields").GetProperty("EID").GetString());
            var visible = await DocumentFileHttpTestRequests.ReadAsync(fixture, reader, Point(fixture, "document.document.read"), FilesUrl(first));
            Assert.Single(visible.GetProperty("files").EnumerateArray());
            using var noFeature = await DocumentFileHttpTestRequests.SendAsync(fixture, reader, Point(fixture, "document.document.download"), HttpMethod.Get, ContentUrl(first));
            Assert.Equal(HttpStatusCode.Forbidden, noFeature.StatusCode);
        }
        await SaveRuleAsync(fixture, admin, "NoDownload", "document.document", "EID", first.DocumentId, "download");
        var list = await DocumentFileHttpTestRequests.ReadAsync(fixture, ordinary, Point(fixture, "document.document.read"), FilesUrl(first));
        Assert.False(list.GetProperty("downloadAllowed").GetBoolean());
        var file = Assert.Single(list.GetProperty("files").EnumerateArray());
        Assert.Equal("main.pdf", file.GetProperty("fileName").GetString());
        foreach (var hidden in new[] { "fileId", "usageId", "storageKey", "storagePath", "actorId", "ownerKey" }) Assert.False(file.TryGetProperty(hidden, out _));
        using (var denied = await DocumentFileHttpTestRequests.SendAsync(fixture, ordinary, Point(fixture, "document.document.download"), HttpMethod.Get, ContentUrl(first)))
            await ProblemAsync(denied, HttpStatusCode.Forbidden, "Document.DownloadDenied");
        using (var wrongTarget = await DocumentFileHttpTestRequests.SendAsync(fixture, admin, Point(fixture, "document.document.download"), HttpMethod.Get,
            $"/api/document/documents/{first.DocumentId}/files/{second.LinkId}/content"))
            await ProblemAsync(wrongTarget, HttpStatusCode.NotFound, "Files.UsageUnavailable");
        await SaveRuleAsync(fixture, admin, "HiddenDocument", "document.document", "EID", first.DocumentId, "read");
        foreach (var (url, behavior) in new[] { (FilesUrl(first), "document.document.read"), (ContentUrl(first), "document.document.download") })
        {
            using var hidden = await DocumentFileHttpTestRequests.SendAsync(fixture, ordinary, Point(fixture, behavior), HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        await SaveRuleAsync(fixture, admin, "HiddenCreated", "document.document", "ECODE", "UNREADABLE-CREATED", "read");
        var values = new { ECODE = "UNREADABLE-CREATED", ENAME = "创建后不可读" };
        var staged = await StageAsync(fixture, ordinary, position, values, [7, 8]);
        using var saved = await SaveMainAsync(fixture, ordinary, position, values, staged.GetProperty("uploadId").GetString()!);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var receipt = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.Single(receipt.EnumerateObject());
        var created = await ReadCreatedAsync(fixture, values.ECODE);
        using var unreadable = await DocumentFileHttpTestRequests.SendAsync(fixture, ordinary, Point(fixture, "document.document.read"), HttpMethod.Get,
            $"/api/document/documents/{created.DocumentId}/files");
        Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
        Assert.Equal(3, (await ObserveBusinessAsync(fixture)).Usages.Length);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "可信关联互换与缺失内容显式失败，原裸Files入口仍拒绝")]
    public async Task AssociationAndContent_RejectCorruptionAndMissingBlobWithoutRawBypass()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var first = await CreateMainAsync(fixture, admin, position, "PAIR-01", [1, 2, 3]);
        var second = await CreateMainAsync(fixture, admin, position, "PAIR-02", [4, 5, 6]);
        foreach (var (model, keyField, key, field, invalid, original, error) in new[]
        {
            ("DocumentFileLink", "LID", first.LinkId, "FileUsageId", second.UsageId, first.UsageId, "Files.PersistentAssociationCorrupted"),
            ("FileUsage", "EID", first.UsageId, "FileId", second.FileId, first.FileId, "Files.PersistentAssociationCorrupted"),
            ("FileUploadLease", "EID", first.UploadId, "FileId", second.FileId, first.FileId, "Files.PersistentAssociationCorrupted"),
            ("DocumentFileLink", "LID", first.LinkId, "Role", "attachment", "main", "Document.FileAssociationCorrupted")
        })
        {
            await ChangeRowAsync(fixture, model, keyField, key, field, invalid);
            await IntegrityFailureAsync(() => DocumentFileHttpTestRequests.SendAsync(fixture, admin, Point(fixture, "document.document.read"), HttpMethod.Get, FilesUrl(first)), error);
            await IntegrityFailureAsync(() => DocumentFileHttpTestRequests.SendAsync(fixture, admin, Point(fixture, "document.document.download"), HttpMethod.Get, ContentUrl(first)), error);
            await ChangeRowAsync(fixture, model, keyField, key, field, original);
        }
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, "/api/files/" + first.FileId), (HttpMethod.Get, "/api/files/" + first.FileId + "/download"),
            (HttpMethod.Delete, "/api/files/" + first.FileId), (HttpMethod.Post, "/api/files/upload")
        })
        {
            using var raw = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "document.document.download"), method, path);
            Assert.Equal(HttpStatusCode.Forbidden, raw.StatusCode);
        }
        using (var arbitrary = await DocumentFileHttpTestRequests.SendAsync(fixture, admin, Point(fixture, "document.document.read"), HttpMethod.Get, FilesUrl(first) + "?usageId=" + second.UsageId))
            await ProblemAsync(arbitrary, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        var facts = await ObserveBusinessAsync(fixture);
        var content = ContentPath(fixture, Assert.Single(facts.Files, row => Equals(row["EID"], first.FileId)));
        File.Delete(content);
        await IntegrityFailureAsync(() => DocumentFileHttpTestRequests.SendAsync(fixture, admin, Point(fixture, "document.document.download"), HttpMethod.Get, ContentUrl(first)), "Files.ContentMissing");
        Assert.Equal("active", Assert.Single((await ObserveBusinessAsync(fixture)).Usages, row => Equals(row["EID"], first.UsageId))["State"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实业务commit前失败全回滚可修正，commit后未知保留全部已提交关联和字节")]
    public async Task BusinessCommitFailure_RechecksFreshFactsAndPreservesKnownStagedOrCommittedContent()
    {
        foreach (var afterCommit in new[] { false, true })
        {
            var fault = new BusinessCommitFault(afterCommit);
            await using var fixture = await ControlledFixtureAsync(fault);
            using var admin = await fixture.LoginAdministratorAsync();
            var position = await ContentAsync(fixture, admin);
            var values = new { ECODE = "COMMIT-" + afterCommit, ENAME = "业务提交结果" };
            byte[] bytes = [1, 2, 3, 4];
            var staged = await StageAsync(fixture, admin, position, values, bytes);
            var uploadId = staged.GetProperty("uploadId").GetString()!;
            fault.Armed = true;
            if (afterCommit)
            {
                using var unknown = await SaveMainAsync(fixture, admin, position, values, uploadId);
                await ProblemAsync(unknown, HttpStatusCode.ServiceUnavailable, "Persistence.CommitOutcomeUnknown");
            }
            else await InjectedFailureAsync(() => SaveMainAsync(fixture, admin, position, values, uploadId));
            Assert.Equal(1, fault.Injected);
            Assert.True(fault.ObservedCompleteBusinessTracker);
            var durable = await ObserveBusinessAsync(fixture);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(ContentPath(fixture, Assert.Single(durable.Files))));
            Assert.Equal(afterCommit ? "confirmed" : "staged", Assert.Single(durable.Leases)["State"]);
            Assert.Equal(afterCommit ? 1 : 0, durable.Documents.Length);
            Assert.Equal(afterCommit ? 1 : 0, durable.Versions.Length);
            Assert.Equal(afterCommit ? 1 : 0, durable.Iterations.Length);
            Assert.Equal(afterCommit ? 1 : 0, durable.Links.Length);
            Assert.Equal(afterCommit ? 1 : 0, durable.Usages.Length);
            Assert.Equal(afterCommit ? 1 : 0, durable.References.Length);
            if (afterCommit)
            {
                Assert.Equal("active", Assert.Single(durable.Usages)["State"]);
                using var repeated = await SaveMainAsync(fixture, admin, position, values, uploadId);
                await ProblemAsync(repeated, HttpStatusCode.Conflict, "Files.UploadAlreadyConfirmed");
                Assert.Single((await ObserveBusinessAsync(fixture)).Documents);
            }
            else
            {
                using var corrected = await SaveMainAsync(fixture, admin, position, new { ECODE = "COMMIT-CORRECTED", ENAME = "修正后保存" }, uploadId);
                Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
                Assert.Equal("confirmed", Assert.Single((await ObserveBusinessAsync(fixture)).Leases)["State"]);
            }
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实HTTP在业务提交前取消，全部关联回滚且合法主文件可修正重试")]
    public async Task BusinessCancellation_BeforeCommitRollsBackAllRowsAndKeepsMainAvailableForRetry()
    {
        var fault = new BusinessCommitFault(false) { PauseBeforeCommit = true };
        await using var fixture = await ControlledFixtureAsync(fault);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var values = new { ECODE = "CANCEL-COMMIT", ENAME = "提交前取消" };
        byte[] bytes = [1, 2, 3, 4];
        var staged = await StageAsync(fixture, admin, position, values, bytes);
        var uploadId = staged.GetProperty("uploadId").GetString()!;
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/documents")
        { Content = new StringContent(JsonSerializer.Serialize(new { position, values, uploads = new { mainUploadId = uploadId } }), Encoding.UTF8, "application/json") };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
        request.Headers.Add("X-NewP-Data-Declaration", WorkspaceObjectDeclaration(fixture, "workspace.content.createDocument", "workspace.folder.createDocument").Id);
        fault.Armed = true;
        var saving = admin.SendAsync(request, cancellation.Token);
        Exception? primary = null;
        try
        {
            await fault.CommitReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(fault.ObservedCompleteBusinessTracker);
            cancellation.Cancel();
            await fault.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await fault.RollbackObserved.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception exception) { primary = exception; }
        finally
        {
            cancellation.Cancel();
            fault.Resume.TrySetResult();
            try { using var late = await saving.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { primary ??= exception; }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        Assert.True(saving.IsCanceled);
        Assert.Equal(0, fault.Injected);
        var durable = await ObserveBusinessAsync(fixture);
        Assert.Empty(durable.Documents); Assert.Empty(durable.Versions); Assert.Empty(durable.Iterations);
        Assert.Empty(durable.Links); Assert.Empty(durable.Usages); Assert.Empty(durable.References);
        Assert.Equal("staged", Assert.Single(durable.Leases)["State"]);
        Assert.Equal("ready", Assert.Single(durable.Files)["State"]);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ContentPath(fixture, Assert.Single(durable.Files))));
        using var corrected = await SaveMainAsync(fixture, admin, position, new { ECODE = "CANCEL-COMMIT-RETRY", ENAME = "取消后修正重试" }, uploadId);
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        Assert.Equal("active", Assert.Single((await ObserveBusinessAsync(fixture)).Usages)["State"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace实际关联写入在确认后失败，Document全图和Files使用同事务回滚且可重试")]
    public async Task WorkspaceReferenceFailure_RollsBackConfirmedUseAndAllowsSameMainRetry()
    {
        var fault = new BusinessCommandFault { FailReferenceInsert = true };
        await using var fixture = await ControlledFixtureAsync(fault);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var values = new { ECODE = "REFERENCE-01", ENAME = "目录关联原子保存" };
        var staged = await StageAsync(fixture, admin, position, values, [1, 2, 3]);
        var uploadId = staged.GetProperty("uploadId").GetString()!;
        fault.Armed = true;
        await InjectedFailureAsync(() => SaveMainAsync(fixture, admin, position, values, uploadId));
        Assert.Equal(1, fault.Injected);
        Assert.True(fault.ObservedActiveConfirmation);
        var durable = await ObserveBusinessAsync(fixture);
        Assert.Empty(durable.Documents); Assert.Empty(durable.Versions); Assert.Empty(durable.Iterations);
        Assert.Empty(durable.Links); Assert.Empty(durable.Usages); Assert.Empty(durable.References);
        Assert.Equal("staged", Assert.Single(durable.Leases)["State"]);
        Assert.True(File.Exists(ContentPath(fixture, Assert.Single(durable.Files))));
        using var retried = await SaveMainAsync(fixture, admin, position, values, uploadId);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("active", Assert.Single((await ObserveBusinessAsync(fixture)).Usages)["State"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Document own单删和Workspace批删后项真实SQL失败，所有使用和内容均保留")]
    public async Task OwnAndBulkDelete_LaterSqlFailureRollsBackReleaseAndAllBusinessRows()
    {
        var fault = new BusinessCommandFault();
        await using var fixture = await ControlledFixtureAsync(fault);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var first = await CreateMainAsync(fixture, admin, position, "DELETE-01", [1, 2, 3]);
        var second = await CreateMainAsync(fixture, admin, position, "DELETE-02", [4, 5, 6]);
        fault.ArmDelete(1);
        await InjectedFailureAsync(() => FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + first.WorkspaceLinkId, new { position }, WorkspaceObjectDeclaration(fixture, "workspace.folder.deleteObject", "workspace.folder.deleteObject")));
        Assert.Equal(1, fault.Injected);
        Assert.True(fault.ObservedReleasedUsage);
        await AssertTwoActiveDocumentsAsync(fixture);
        fault.ArmDelete(2);
        await InjectedFailureAsync(() => FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObjects"), HttpMethod.Post,
            "/api/workspace/content/delete", new { position, linkIds = new[] { first.WorkspaceLinkId, second.WorkspaceLinkId } }, WorkspaceObjectDeclaration(fixture, "workspace.folder.deleteObjects", "workspace.folder.deleteObjects")));
        Assert.Equal(2, fault.Injected);
        Assert.Equal(2, fault.DocumentDeleteCommands);
        Assert.True(fault.ObservedReleasedUsage);
        await AssertTwoActiveDocumentsAsync(fixture);
        using var deleted = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObjects"), HttpMethod.Post,
            "/api/workspace/content/delete", new { position, linkIds = new[] { first.WorkspaceLinkId, second.WorkspaceLinkId } }, WorkspaceObjectDeclaration(fixture, "workspace.folder.deleteObjects", "workspace.folder.deleteObjects"));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var final = await ObserveBusinessAsync(fixture);
        Assert.Empty(final.Documents); Assert.Empty(final.Versions); Assert.Empty(final.Iterations); Assert.Empty(final.Links); Assert.Empty(final.References);
        Assert.All(final.Usages, row => Assert.Equal("released", row["State"]));
        Assert.All(final.Files, row => Assert.True(File.Exists(ContentPath(fixture, row))));
    }

    private static Task<HttpResponseMessage> SaveMainAsync(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition position, object values, string uploadId) =>
        CreateDocumentAsync(fixture, client, new { position, values, uploads = new { mainUploadId = uploadId } });

    private static Task<BusinessAuditHostFixture> ControlledFixtureAsync(IInterceptor? interceptor = null) => BusinessAuditHostFixture.CreateAsync(services =>
    {
        services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(FileCleanupWorker)));
        if (interceptor is not null) services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(interceptor));
    }, includeWorkspace: true);

    private static Task<HttpTestActor> ActorAsync(BusinessAuditHostFixture fixture, HttpClient admin, string name) =>
        HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, name, name, name.ToLowerInvariant(), name,
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.createDocument"),
            Point(fixture, "document.document.read"), Point(fixture, "document.document.download"));

    private static async Task SaveRuleAsync(BusinessAuditHostFixture fixture, HttpClient admin, string id, string resource, string field, string value, string action)
    {
        var rule = new DataScopeRuleDto(id, id, null, id, true, resource, new("attribute", field, "equal", JsonSerializer.SerializeToElement(value)),
            [new(id + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = "deny" })], []);
        using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/" + id, rule);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<(WorkspacePosition Position, string FolderId)> CreateFolderPositionAsync(BusinessAuditHostFixture fixture, HttpClient admin, WorkspacePosition root, string name)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position = root, values = new { ENAME = name } },
            WorkspaceObjectDeclaration(fixture, "workspace.folder.create", "workspace.folder.create"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetString()!;
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "CEID") == id && EF.Property<string>(row, "PEID") == root.RootFolderId);
        return (root with { LinkIds = [(string)link["LID"]!] }, id);
    }

    private sealed record CreatedMain(string DocumentId, string LinkId, string UsageId, string FileId, string UploadId, string WorkspaceLinkId);
    private static async Task<CreatedMain> CreateMainAsync(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition position, string code, byte[] bytes)
    {
        var values = new { ECODE = code, ENAME = "真实主文件-" + code };
        var staged = await StageAsync(fixture, client, position, values, bytes);
        var uploadId = staged.GetProperty("uploadId").GetString()!;
        using var saved = await SaveMainAsync(fixture, client, position, values, uploadId);
        Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var created = await ReadCreatedAsync(fixture, code);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var link = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "PEID") == created.IterationId);
        var ws = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "CEID") == created.DocumentId);
        return new(created.DocumentId, (string)link["LID"]!, (string)link["FileUsageId"]!, (string)link["CEID"]!, uploadId, (string)ws["LID"]!);
    }

    private static string FilesUrl(CreatedMain item) => $"/api/document/documents/{item.DocumentId}/files";
    private static string ContentUrl(CreatedMain item) => $"/api/document/documents/{item.DocumentId}/files/{item.LinkId}/content";
    private static string ContentPath(BusinessAuditHostFixture fixture, IReadOnlyDictionary<string, object?> file)
    {
        var path = Path.GetFullPath(Path.Combine(fixture.StorageRoot, (string)file["StorageKey"]!));
        Assert.StartsWith(Path.GetFullPath(fixture.StorageRoot) + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        return path;
    }

    // 损坏仅注入测试自有行；随后请求仍走真实业务授权、关联核验及SQL读取。
    private static async Task ChangeRowAsync(BusinessAuditHostFixture fixture, string model, string keyField, string key, string field, object? value)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var row = await db.Set<Dictionary<string, object?>>(model).SingleAsync(row => EF.Property<string>(row, keyField) == key);
        row[field] = value;
        await db.SaveChangesAsync();
    }

    private sealed record BusinessFacts(Dictionary<string, object?>[] Documents, Dictionary<string, object?>[] Versions,
        Dictionary<string, object?>[] Iterations, Dictionary<string, object?>[] Links, Dictionary<string, object?>[] References,
        Dictionary<string, object?>[] Files, Dictionary<string, object?>[] Leases, Dictionary<string, object?>[] Usages);
    // 所有持久裁决来自失败请求退出后的新scope，不能观察其旧tracker。
    private static async Task<BusinessFacts> ObserveBusinessAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await AssertContentContainerUnchangedAsync(fixture);
        return new(await db.Set<Dictionary<string, object?>>("Document").AsNoTracking().ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("DocumentVersion").AsNoTracking().ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("DocumentIteration").AsNoTracking().ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().Where(row => EF.Property<string>(row, "ChildModelKey") == "Document").ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("FileRecord").AsNoTracking().ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("FileUsage").AsNoTracking().ToArrayAsync());
    }

    private static async Task AssertTwoActiveDocumentsAsync(BusinessAuditHostFixture fixture)
    {
        var durable = await ObserveBusinessAsync(fixture);
        Assert.Equal(2, durable.Documents.Length); Assert.Equal(2, durable.Versions.Length); Assert.Equal(2, durable.Iterations.Length);
        Assert.Equal(2, durable.Links.Length); Assert.Equal(2, durable.References.Length); Assert.Equal(2, durable.Usages.Length);
        Assert.All(durable.Usages, row => Assert.Equal("active", row["State"]));
        Assert.All(durable.Leases, row => Assert.Equal("confirmed", row["State"]));
        Assert.All(durable.Files, row => { Assert.Equal("ready", row["State"]); Assert.True(File.Exists(ContentPath(fixture, row))); });
    }

    private static async Task IntegrityFailureAsync(Func<Task<HttpResponseMessage>> send, string error)
    {
        HttpResponseMessage? response = null;
        Exception? failure = null;
        try { response = await send(); }
        catch (Exception exception) { failure = exception; }
        using (response)
        {
            if (failure is not null) Assert.Contains(error, failure.ToString(), StringComparison.Ordinal);
            else { Assert.NotNull(response); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); }
        }
    }

    private static async Task InjectedFailureAsync(Func<Task<HttpResponseMessage>> send)
    {
        HttpResponseMessage? response = null;
        Exception? failure = null;
        try { response = await send(); }
        catch (Exception exception) { failure = exception; }
        using (response)
        {
            if (failure is not null) Assert.Contains(nameof(InjectedBusinessFailure), failure.ToString(), StringComparison.Ordinal);
            else { Assert.NotNull(response); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); }
        }
    }

    private sealed class InjectedBusinessFailure : Exception { }
    private sealed class BusinessCommitFault(bool afterCommit) : DbTransactionInterceptor
    {
        private Guid _target;
        public bool Armed { get; set; }
        public bool PauseBeforeCommit { get; init; }
        public TaskCompletionSource CommitReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RollbackObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Injected { get; private set; }
        public bool ObservedCompleteBusinessTracker { get; private set; }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context?.ChangeTracker.Entries().ToArray() ?? [];
            if (Armed && entries.Any(entry => entry.Metadata.Name == "DocumentFileLink") && entries.Any(entry => entry.Metadata.Name == "FileUsage"))
            {
                ObservedCompleteBusinessTracker = new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileUsage", "FileUploadLease", "WorkspaceFolderContent" }
                    .All(model => entries.Any(entry => entry.Metadata.Name == model));
                _target = eventData.TransactionId;
                Armed = false;
                if (PauseBeforeCommit)
                {
                    CommitReached.TrySetResult();
                    try { await Resume.Task.WaitAsync(cancellationToken); }
                    catch (OperationCanceledException) { CancellationObserved.TrySetResult(); throw; }
                    // 失败清理也释放屏障，但不能因测试释放而意外提交。
                    throw new InvalidOperationException("Commit cancellation barrier released without observing cancellation.");
                }
                if (!afterCommit) { Injected++; throw new InjectedBusinessFailure(); }
            }
            return result;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (afterCommit && _target == eventData.TransactionId) { _target = Guid.Empty; Injected++; throw new InjectedBusinessFailure(); }
            return Task.CompletedTask;
        }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (PauseBeforeCommit && _target == eventData.TransactionId) RollbackObserved.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class BusinessCommandFault : DbCommandInterceptor
    {
        private int _targetDelete;
        public bool Armed { get; set; }
        public bool FailReferenceInsert { get; init; }
        public int Injected { get; private set; }
        public int DocumentDeleteCommands { get; private set; }
        public bool ObservedActiveConfirmation { get; private set; }
        public bool ObservedReleasedUsage { get; private set; }
        public void ArmDelete(int ordinal) { _targetDelete = ordinal; DocumentDeleteCommands = 0; ObservedReleasedUsage = false; Armed = true; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Observe(command, eventData); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Observe(command, eventData); return ValueTask.FromResult(result); }
        private void Observe(DbCommand command, CommandEventData eventData)
        {
            if (!Armed) return;
            var insert = FailReferenceInsert && command.CommandText.Contains("[WorkspaceFolderContent]", StringComparison.Ordinal)
                && (command.CommandText.Contains("INSERT", StringComparison.Ordinal) || command.CommandText.Contains("MERGE", StringComparison.Ordinal));
            var delete = _targetDelete > 0 && command.CommandText.Contains("[Document]", StringComparison.Ordinal)
                && command.CommandText.Contains("DELETE", StringComparison.Ordinal) && ++DocumentDeleteCommands == _targetDelete;
            if (!insert && !delete) return;
            var entries = eventData.Context?.ChangeTracker.Entries().ToArray() ?? [];
            ObservedActiveConfirmation = entries.Any(entry => entry.Metadata.Name == "FileUsage" && Equals(entry.Property("State").CurrentValue, "active"))
                && entries.Any(entry => entry.Metadata.Name == "FileUploadLease" && Equals(entry.Property("State").CurrentValue, "confirmed"));
            ObservedReleasedUsage = entries.Any(entry => entry.Metadata.Name == "FileUsage" && Equals(entry.Property("State").CurrentValue, "released"));
            Armed = false;
            Injected++;
            throw new InjectedBusinessFailure();
        }
    }

    // 正常创建取实际Workspace入口声明；不改变手工构造的缺声明/伪造凭据反例。
    private static Task<HttpResponseMessage> CreateDocumentAsync(BusinessAuditHostFixture fixture, HttpClient client, object input) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Post, "/api/workspace/content/documents", input,
            WorkspaceObjectDeclaration(fixture, "workspace.content.createDocument", "workspace.folder.createDocument"));

    private static ICP.Platform.Kernel.ModulePlanning.ActionDataAccessRequirement WorkspaceObjectDeclaration(
        BusinessAuditHostFixture fixture, string execution, string behavior) =>
        Assert.Single(fixture.Services.GetRequiredService<ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlan>().ActionDataAccessCatalog,
            item => (item.Execution == execution || item.BusinessBinding == execution) && item.FeaturePositions.Contains(Point(fixture, behavior).Position));

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }

    private static HttpRequestMessage MultipartRequest(BusinessAuditHostFixture fixture, (string Name, string Value)[] parts,
        byte[] bytes, string name, bool extra = false)
    {
        var content = new MultipartFormDataContent();
        foreach (var part in parts) content.Add(new StringContent(part.Value, Encoding.UTF8, "application/json"), part.Name);
        content.Add(new ByteArrayContent(bytes), "file", name);
        if (extra) content.Add(new StringContent("{}", Encoding.UTF8, "application/json"), "extra");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads") { Content = content };
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
        return request;
    }

    private static async Task<(string DocumentId, string IterationId)> ReadCreatedAsync(BusinessAuditHostFixture fixture, string code)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var master = await db.Set<Dictionary<string, object?>>("Document").SingleAsync(row => EF.Property<string>(row, "ECODE") == code);
        var version = await db.Set<Dictionary<string, object?>>("DocumentVersion").SingleAsync(row => EF.Property<string>(row, "EID") == (string)master["LatestVersionId"]!);
        return ((string)master["EID"]!, (string)version["CurrentIterationId"]!);
    }

    // 原 HTTP 场景消费列表位置；树菜单的同一行为拥有独立功能位置。
    internal static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) => Assert.Single(fixture.FeatureCatalog,
        point => point.BehaviorKey == behavior && (behavior is not ("workspace.folder.remove" or "workspace.folder.editObject")
            || point.ModuleId == "Workspace" && point.StaticScope == "folder" && point.ActionScope == "list.rowActions"
                && point.Key == (behavior == "workspace.folder.remove" ? "remove" : "edit")));
    internal static async Task<WorkspacePosition> RootAsync(BusinessAuditHostFixture fixture, HttpClient admin)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.spaces.access"), HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
    }

    private sealed record ContentContainer(string LinkId, string Snapshot);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BusinessAuditHostFixture, ContentContainer> ContentContainers = new();

    // 新上传/保存沿正式普通文件夹准备；根本身仍由RootAsync服务祖先与失效场景。
    internal static async Task<WorkspacePosition> ContentAsync(BusinessAuditHostFixture fixture, HttpClient admin)
    {
        var root = await RootAsync(fixture, admin);
        var container = await CreateFolderPositionAsync(fixture, admin, root, "Document consumer container");
        using var scope = fixture.Services.CreateScope();
        var link = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
            .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "LID") == container.Position.LinkIds[0]);
        ContentContainers.Add(fixture, new(container.Position.LinkIds[0], JsonSerializer.Serialize(link.OrderBy(pair => pair.Key).ToDictionary())));
        return container.Position;
    }

    // 业务Link计数不包含准备目录，同时以完整身份/次序/审计快照证明它未被业务操作误删或修改。
    internal static async Task AssertContentContainerUnchangedAsync(BusinessAuditHostFixture fixture)
    {
        if (!ContentContainers.TryGetValue(fixture, out var container)) return;
        using var scope = fixture.Services.CreateScope();
        var link = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
            .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "LID") == container.LinkId);
        Assert.Equal(container.Snapshot, JsonSerializer.Serialize(link.OrderBy(pair => pair.Key).ToDictionary()));
    }
    internal static async Task<JsonElement> StageAsync(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition position,
        object values, byte[] bytes, string name = "main.pdf")
    {
        using var request = UploadRequest(fixture, position, values, new ByteArrayContent(bytes), name);
        request.Headers.Add("X-NewP-Data-Declaration", WorkspaceObjectDeclaration(fixture, "workspace.content.createDocument", "workspace.folder.createDocument").Id);
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    internal static HttpRequestMessage UploadRequest(BusinessAuditHostFixture fixture, WorkspacePosition position, object values, HttpContent content, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/document-uploads");
        request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder.createDocument").Position);
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(position), Encoding.UTF8, "application/json"), "position");
        multipart.Add(new StringContent(JsonSerializer.Serialize(values), Encoding.UTF8, "application/json"), "values");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(content, "file", name);
        request.Content = multipart;
        return request;
    }
}
