using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DocumentFilesSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实下载要求独立正式声明，沿文件回链裁决并从相同合同派生下载反馈")]
    public async Task Download_RequiresBoundDeclarationAndTrustedOwnerChain()
    {
        await using var fixture = await ControlledFixtureAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var first = await CreateMainAsync(fixture, admin, position, "DECLARATION-313-1", [1, 2, 3]);
        var second = await CreateMainAsync(fixture, admin, position, "DECLARATION-313-2", [4, 5, 6]);
        var actor = await ActorAsync(fixture, admin, "DownloadDeclaration313");
        using var ordinary = actor.Client;
        var readFeature = Point(fixture, "document.document.read");
        var downloadFeature = Point(fixture, "document.document.download");
        // 首个反例必须先经过真实已授权对象与文件准备，不能因缺fixture或错误目标提前拒绝。
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, downloadFeature, HttpMethod.Get, ContentUrl(first)))
            await ProblemAsync(missing, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var download = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.files.download"
            && item.FeaturePositions.Contains(downloadFeature.Position));
        var read = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.read"
            && item.FeaturePositions.Contains(readFeature.Position));
        using (var allowed = await FeatureHttpTestRequests.SendAsync(ordinary, downloadFeature, HttpMethod.Get, ContentUrl(first), dataAccess: download))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal(new byte[] { 1, 2, 3 }, await allowed.Content.ReadAsByteArrayAsync());
        }
        using (var crossed = await FeatureHttpTestRequests.SendAsync(ordinary, downloadFeature, HttpMethod.Get, ContentUrl(first), dataAccess: read))
            await ProblemAsync(crossed, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        using (var request = new HttpRequestMessage(HttpMethod.Get, ContentUrl(first)))
        {
            request.Headers.Add("X-NewP-Feature", downloadFeature.Position);
            request.Headers.Add("X-NewP-Data-Declaration", "Document/view:unknown/featureActions/action:download");
            using var unknown = await ordinary.SendAsync(request);
            await ProblemAsync(unknown, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        }
        using (var wrong = await FeatureHttpTestRequests.SendAsync(ordinary, downloadFeature, HttpMethod.Get,
            $"/api/document/documents/{first.DocumentId}/files/{second.LinkId}/content", dataAccess: download))
            await ProblemAsync(wrong, HttpStatusCode.NotFound, "Files.UsageUnavailable");
        using (var lidAsDocument = await FeatureHttpTestRequests.SendAsync(ordinary, downloadFeature, HttpMethod.Get,
            $"/api/document/documents/{first.LinkId}/files/{first.LinkId}/content", dataAccess: download))
            await ProblemAsync(lidAsDocument, HttpStatusCode.NotFound, "Files.UsageUnavailable");
        var readOnly = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "FileReadOnly313", "只读文件入口", "file-read-only313", "只读文件主体", readFeature);
        using (var reader = readOnly.Client)
        {
            using var metadata = await FeatureHttpTestRequests.SendAsync(reader, readFeature, HttpMethod.Get, FilesUrl(first), dataAccess: read);
            Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
            Assert.True((await metadata.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("downloadAllowed").GetBoolean());
            using var noDownloadFeature = await FeatureHttpTestRequests.SendAsync(reader, downloadFeature, HttpMethod.Get, ContentUrl(first), dataAccess: download);
            Assert.Equal(HttpStatusCode.Forbidden, noDownloadFeature.StatusCode);
        }
        await AssertFeedbackAsync(true);
        await SaveRuleAsync(fixture, admin, "DownloadDeclaration313Denied", "document.document", "EID", first.DocumentId, "download");
        await AssertFeedbackAsync(false);
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, downloadFeature, HttpMethod.Get, ContentUrl(first), dataAccess: download))
            await ProblemAsync(denied, HttpStatusCode.Forbidden, "Document.DownloadDenied");
        await SaveRuleAsync(fixture, admin, "DownloadDeclaration313Hidden", "document.document", "EID", first.DocumentId, "read");
        foreach (var (url, feature, binding) in new[] { (FilesUrl(first), readFeature, read), (ContentUrl(first), downloadFeature, download) })
        {
            using var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, url, dataAccess: binding);
            await ProblemAsync(hidden, HttpStatusCode.NotFound, "Files.UsageUnavailable");
        }

        async Task AssertFeedbackAsync(bool expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, readFeature, HttpMethod.Get, FilesUrl(first), dataAccess: read);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expected, body.GetProperty("downloadAllowed").GetBoolean());
            var file = Assert.Single(body.GetProperty("files").EnumerateArray());
            Assert.Equal(first.LinkId, file.GetProperty("fileLinkId").GetString());
            Assert.False(file.TryGetProperty("fileId", out _));
        }
    }
}
