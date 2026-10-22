using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using static ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DocumentDownloadBoundarySqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "实际按钮初检允许后撤权仍在owner回链时拒绝，依赖故障保留异常而不降级成拒绝")]
    public async Task DownloadPolicy_RechecksCurrentQualificationAndPreservesBusinessFaultAfterButtonAllows()
    {
        var probe = new DownloadProbe();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.Remove(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(FileCleanupWorker)));
            services.Replace(ServiceDescriptor.Scoped<IObjectActionAccess>(provider =>
                new ObservedActions(ActivatorUtilities.CreateInstance<ObjectActionAccess>(provider), probe)));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DownloadBoundary333", "下载边界主体", "download-boundary333", "下载边界主体",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.createDocument"),
            Point(fixture, "document.document.read"), Point(fixture, "document.document.download"));
        using var ordinary = actor.Client;
        var feature = Point(fixture, "workspace.folder.createDocument");
        var values = new { ECODE = "DOWNLOAD-POLICY-333", ENAME = "下载第二时点" };
        var stage = await StageAsync(fixture, ordinary, position, values, [1, 2, 3]);
        using (var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, feature, HttpMethod.Post, "/api/workspace/content/documents",
            new { position, values, uploads = new { mainUploadId = stage.GetProperty("uploadId").GetString() } })) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        string documentId, linkId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            documentId = (string)(await db.Set<Dictionary<string, object?>>("Document").AsNoTracking().SingleAsync())["EID"]!;
            linkId = (string)(await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().SingleAsync())["LID"]!;
        }
        // 使用真实独立规则API在普通主体初检完成与policy检查之间撤销资格。
        probe.Arm(documentId);
        var first = DownloadAsync();
        try
        {
            await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await SaveRuleAsync(documentId);
        }
        finally { probe.Resume.TrySetResult(); }
        using (var denied = await first)
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("Document.DownloadDenied", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Equal(2, probe.Calls);
        Assert.Equal(DataExecutionDecision.Denied, probe.LastDecision);
        await SaveRuleAsync("another-document");
        // 两次真实判定都允许后，在第二时点注入基础设施故障；不能翻译成403、false或空结果。
        const string faultMarker = "document-download-policy-dependency-333";
        probe.Arm(documentId, new InvalidOperationException(faultMarker));
        var second = DownloadAsync();
        try { await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { probe.Resume.TrySetResult(); }
        HttpResponseMessage? failedResponse = null;
        Exception? failure = null;
        try { failedResponse = await second; }
        catch (Exception error) { failure = error; }
        using (failedResponse)
        {
            if (failure is not null) Assert.Contains(faultMarker, failure.ToString(), StringComparison.Ordinal);
            else { Assert.NotNull(failedResponse); Assert.Equal(HttpStatusCode.InternalServerError, failedResponse.StatusCode); }
        }
        Assert.Equal(2, probe.Calls);
        Assert.Equal(DataExecutionDecision.Allowed, probe.LastDecision);
        using var final = fixture.Services.CreateScope();
        var finalDb = final.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Equal("active", (await finalDb.Set<Dictionary<string, object?>>("FileUsage").AsNoTracking().SingleAsync())["State"]);
        Assert.Equal("confirmed", (await finalDb.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().SingleAsync())["State"]);

        Task<HttpResponseMessage> DownloadAsync() => new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary,
            Point(fixture, "document.document.download"), HttpMethod.Get, $"/api/document/documents/{documentId}/files/{linkId}/content");
        async Task SaveRuleAsync(string target)
        {
            var rule = new DataScopeRuleDto("DownloadPolicy333", "DownloadPolicy333", null, "DownloadPolicy333", true, "document.document",
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(target)),
                [new("DownloadPolicy333Subject", "all", null, 0, new Dictionary<string, string> { ["download"] = "deny" })], []);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
                "/api/authorization/data-scope/rules/DownloadPolicy333", rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    // 仅观察实际IObjectActionAccess；所有裁决仍由真实服务完成，屏障不携带允许结果。
    private sealed class DownloadProbe
    {
        private string? target;
        private Exception? failure;
        internal int Calls;
        internal DataExecutionDecision LastDecision;
        internal TaskCompletionSource Reached { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm(string id, Exception? error = null)
        { target = id; failure = error; Calls = 0; Reached = new(TaskCreationOptions.RunContinuationsAsynchronously); Resume = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        internal int Enter(string model, string id, IReadOnlyList<string> actions) => model == "Document" && id == target && actions.Contains("download") ? Interlocked.Increment(ref Calls) : 0;
        internal async Task ObserveAsync(int ordinal, DataExecutionDecision decision, CancellationToken token)
        {
            if (ordinal == 0) return;
            LastDecision = decision;
            if (ordinal == 1 && decision == DataExecutionDecision.Allowed) { Reached.TrySetResult(); await Resume.Task.WaitAsync(token); }
            if (ordinal == 2 && failure is not null) throw failure;
        }
    }
    private sealed class ObservedActions(IObjectActionAccess inner, DownloadProbe probe) : IObjectActionAccess
    {
        public async Task<DataExecutionDecision> CheckAsync(string model, string id, IReadOnlyList<string> actions, CancellationToken token = default)
        { var ordinal = probe.Enter(model, id, actions); var result = await inner.CheckAsync(model, id, actions, token); await probe.ObserveAsync(ordinal, result, token); return result; }
        public Task<DataExecutionDecision> CheckAsync(string model, string id, IReadOnlyList<string> actions, object context, CancellationToken token = default) => inner.CheckAsync(model, id, actions, context, token);
        public Task<DataExecutionDecision> CheckCandidateAsync(string model, IReadOnlyDictionary<string, object?> values, IReadOnlyList<string> actions, CancellationToken token = default) => inner.CheckCandidateAsync(model, values, actions, token);
    }
}
