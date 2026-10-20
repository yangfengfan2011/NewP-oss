using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通主体的真实Part真副本准备拒绝缺失正式声明，不登记恢复记录或创建副本")]
    public async Task PartObjectCopy_RejectsMissingDeclarationAtItsRealPrepareEntry()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "声明来源");
        var target = await Folder(fixture, admin, root, "声明目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "DECLARATION-SOURCE", ENAME = "真实Part来源" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var copy = Point(fixture, "workspace.folder.pasteObjectCopy");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CopyDeclarationActor", "复制声明主体",
            "copy-declaration", "复制声明账号", Point(fixture, "workspace.spaces.access"), copy);
        using var ordinary = actor.Client;
        var input = new { mode = "copy", sourcePosition = source, linkIds = new[] { link["LID"] }, targetPosition = target };
        using var response = await FeatureHttpTestRequests.SendAsync(ordinary, copy, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare",
            input);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActionDataAccess.DeclarationInvalid", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty(await Rows(fixture, "WorkspaceObjectCopy"));
        Assert.Single(await Rows(fixture, "Part"));
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("workspace.objectCopy", copy);
        var sourceId = (string)link["CEID"]!;
        await Rule(fixture, admin, "PartPrepareNoSourceEdit", "part.part", sourceId, "edit", "deny");
        using (var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, copy, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", input, declaration))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            var prepared = await allowed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("", prepared.GetProperty("values").GetProperty("ECODE").GetString());
            Assert.Equal("真实Part来源", prepared.GetProperty("values").GetProperty("ENAME").GetString());
        }
        var alternate = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.copy", Point(fixture, "workspace.folder.copy"));
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, copy, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", input, alternate))
            await Problem(swapped, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        await Rule(fixture, admin, "PartPrepareCopy", "part.part", sourceId, "partObjectCopy", "deny");
        using (var copyDenied = await FeatureHttpTestRequests.SendAsync(ordinary, copy, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", input, declaration))
            await Problem(copyDenied, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        await Rule(fixture, admin, "PartPrepareCopy", "part.part", sourceId, "partObjectCopy", "allow");
        await Rule(fixture, admin, "PartPrepareRead", "part.part", sourceId, "read", "deny");
        using (var readDenied = await FeatureHttpTestRequests.SendAsync(ordinary, copy, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", input, declaration))
            await Problem(readDenied, HttpStatusCode.NotFound, "Workspace.TargetUnavailable");
        await Rule(fixture, admin, "PartPrepareRead", "part.part", sourceId, "read", "allow");
        var parentId = FolderId(target, await Rows(fixture, "WorkspaceFolderContent"));
        await Rule(fixture, admin, "PartPrepareParent", "workspace.folder", parentId, "edit", "deny");
        using (var parentDenied = await FeatureHttpTestRequests.SendAsync(ordinary, copy, HttpMethod.Post,
            "/api/workspace/content/objects/copy/prepare", input, declaration))
            await Problem(parentDenied, HttpStatusCode.Forbidden, "Workspace.ParentWriteDenied");
        Assert.Single(await Rows(fixture, "WorkspaceObjectCopy"));
        Assert.Single(await Rows(fixture, "Part"));
    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Part与Document实际prepare/submit均拒缺失和串用来源，共用声明由真实模型选分支，恢复仍用原凭据")]
    public async Task ObjectCopy_PartAndDocumentRejectCrossedSourcesWithoutChangingRecovery()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "阶段来源");
        var target = await Folder(fixture, admin, root, "阶段目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "STAGE-PART", ENAME = "Part来源" } });
        var part = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var partInput = new { mode = "copy", sourcePosition = source, linkIds = new[] { part["LID"] }, targetPosition = target };
        var prepared = await PartCopyCommand(fixture, admin, PartCopyDataPhase.Prepare, partInput);
        var submit = new { operationId = prepared.GetProperty("operationId").GetString(), values = new { ECODE = "NEVER-PART", ENAME = "不创建" } };
        var point = Point(fixture, "workspace.folder.pasteObjectCopy");
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/content/objects/copy", submit))
            await Problem(missing, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        var wrong = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.copy", Point(fixture, "workspace.folder.copy"));
        using (var swapped = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/content/objects/copy", submit, wrong))
            await Problem(swapped, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        var recovered = await Command(fixture, admin, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { submit.operationId });
        Assert.Equal("ready", recovered.GetProperty("state").GetString());
        Assert.Single(await Rows(fixture, "Part"));

        var values = new { ECODE = "STAGE-DOCUMENT", ENAME = "Document来源" };
        var uploaded = await DocumentFilesSqlServerHttpApiTests.StageAsync(fixture, admin, source, values, [1, 2, 3]);
        await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents",
            new { position = source, values, uploads = new { mainUploadId = uploaded.GetProperty("uploadId").GetString(), attachmentUploadIds = Array.Empty<string>() } });
        var document = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Document"));
        var documentInput = new { mode = "copy", sourcePosition = source, linkIds = new[] { document["LID"] }, targetPosition = target };
        var wrongDeclaration = wrong.Id;
        foreach (var header in new[] { " ", "not-a-declaration", wrongDeclaration })
        {
            using var rejected = await WithDeclaration("objects/copy/prepare", documentInput, header);
            await Problem(rejected, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        }
        var docPrepared = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/prepare", documentInput);
        var docSubmit = new { operationId = docPrepared.GetProperty("operationId").GetString(), values = new { ECODE = "STAGE-DOCUMENT-COPY", ENAME = "正式Document副本" } };
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/content/objects/copy", docSubmit))
            await Problem(missing, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        foreach (var header in new[] { " ", "not-a-declaration", wrongDeclaration })
        {
            using var rejected = await WithDeclaration("objects/copy", docSubmit, header);
            await Problem(rejected, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        }
        Assert.Single(await Rows(fixture, "Document"));
        var copied = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", docSubmit);
        Assert.True(copied.GetProperty("succeeded").GetBoolean());
        Assert.Equal(2, (await Rows(fixture, "Document")).Length);
        Assert.Single(await Rows(fixture, "Part"));

        // TestServer会移除零长度头；使用已证实到达服务端的空白/错误/其他执行声明，不伪称空值已送达。
        async Task<HttpResponseMessage> WithDeclaration(string path, object body, string declaration)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/" + path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-NewP-Feature", point.Position);
            request.Headers.TryAddWithoutValidation("X-NewP-Data-Declaration", declaration);
            return await admin.SendAsync(request);
        }
    }

    [Fact]
    public async Task ObjectCopy_DeclarationTransportReportsActualEmptyHeaderPresence()
    {
        using var server = new TestServer(new WebHostBuilder().Configure(app => app.Run(context =>
            context.Response.WriteAsJsonAsync(new { specified = context.Request.Headers.ContainsKey("X-NewP-Data-Declaration") }))));
        using var client = server.CreateClient();
        foreach (var (kind, value) in new[] { ("empty", ""), ("whitespace", " "), ("invalid", "not-a-declaration") })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/");
            Assert.True(request.Headers.TryAddWithoutValidation("X-NewP-Data-Declaration", value));
            Assert.True(request.Headers.Contains("X-NewP-Data-Declaration"));
            using var response = await client.SendAsync(request);
            var present = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("specified").GetBoolean();
            Console.WriteLine(JsonSerializer.Serialize(new { transport = kind, specified = present }));
            Assert.Equal(kind != "empty", present);
        }
    }

    // 正常Part消费者显式选择真实阶段；恢复/取消仍走原凭据入口，不按URL推断声明。
    private enum PartCopyDataPhase { Prepare, Submit }

    private static Task<HttpResponseMessage> PartCopySend(BusinessAuditHostFixture fixture, HttpClient client,
        PartCopyDataPhase phase, object body)
    {
        var point = Point(fixture, "workspace.folder.pasteObjectCopy");
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("workspace.objectCopy", point);
        return FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post,
            phase == PartCopyDataPhase.Prepare ? "/api/workspace/content/objects/copy/prepare" : "/api/workspace/content/objects/copy", body, declaration);
    }

    private static async Task<JsonElement> PartCopyCommand(BusinessAuditHostFixture fixture, HttpClient client,
        PartCopyDataPhase phase, object body)
    {
        using var response = await PartCopySend(fixture, client, phase, body);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"PartCopy {phase} returned HTTP {(int)response.StatusCode}.");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
