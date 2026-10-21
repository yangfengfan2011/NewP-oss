using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "已提交副本只核当前按钮、功能和操作归属；来源后来删除或撤权不重写持久事实")]
    public async Task PartObjectCopy_CommittedRetryKeepsItsReceiptAfterSourceRemovalAndRevocation()
    {
        var observed = new CopyBoundaryObservation();
        // 只观察真实平台执行器交接的状态，不替换裁决或正式provider。
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddScoped<IButtonBusinessExecutor>(provider => new CopyBoundaryRecorder(provider.GetRequiredService<IButtonExecutionAccess>(), observed)),
            includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "回执来源");
        var target = await Folder(fixture, admin, root, "回执目标");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "RECEIPT-SOURCE", ENAME = "后来删除的源" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Part"));
        var access = Point(fixture, "workspace.spaces.access");
        var copy = Point(fixture, "workspace.folder.pasteObjectCopy");
        var result = Point(fixture, "workspace.folder.resolveObjectCopy");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CommittedCopyActor", "副本持久回执",
            "committed-copy", "持久副本账号", access, copy, result);
        using var ordinary = actor.Client;
        var prepared = await PartCopyCommand(fixture, ordinary, PartCopyDataPhase.Prepare,
            new { mode = "copy", sourcePosition = source, linkIds = new[] { link["LID"] }, targetPosition = target });
        var operationId = prepared.GetProperty("operationId").GetString();
        await PartCopyCommand(fixture, ordinary, PartCopyDataPhase.Submit,
            new { operationId, values = new { ECODE = "RECEIPT-COPY", ENAME = "持久副本" } });
        var copied = Assert.Single(await Rows(fixture, "Part"), row => Equals(row["ECODE"], "RECEIPT-COPY"));
        Assert.Equal(1, observed.Writes);
        var expired = Assert.IsType<WorkspaceObjectCopyState<StandardCrudMutationResult>>(observed.State);
        var callbacks = 0;
        var handler = new WorkspaceObjectCopyHandler();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.WithinBoundaryAsync(expired, "pasteObjectCopy",
            (_, _) => { callbacks++; return Task.FromResult(StandardCrudMutationResult.Success()); }, default));
        Assert.Equal("WORKSPACE_COPY_BOUNDARY_EXPIRED", failure.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(expired, default));
        Assert.Equal(0, callbacks);
        await Rule(fixture, admin, "ReceiptSourceCopyDenied", "part.part", (string)link["CEID"]!, "partObjectCopy", "deny");
        await Rule(fixture, admin, "ReceiptTargetDenied", "workspace.folder", FolderId(target, await Rows(fixture, "WorkspaceFolderContent")), "edit", "deny");
        await Rule(fixture, admin, "ReceiptNewUnreadable", "part.part", (string)copied["EID"]!, "read", "deny");
        var delete = Point(fixture, "workspace.folder.deleteObjects");
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, delete, HttpMethod.Post, "/api/workspace/content/delete",
            new { position = source, linkIds = new[] { link["LID"] } }, new FeatureHttpTestRequests(fixture).Declaration(delete.BehaviorKey, delete)))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Single(await Rows(fixture, "Part"));

        var before = new Dictionary<string, string>();
        foreach (var model in new[] { "Part", "WorkspaceFolderContent", "WorkspaceObjectCopy", "BusinessChange" })
            before[model] = Snapshot(await Rows(fixture, model));
        var retry = new { operationId, values = new { ECODE = "MUST-NOT-REPLAY", ENAME = "不能再次写入" } };
        var receipt = await PartCopyCommand(fixture, ordinary, PartCopyDataPhase.Submit, retry);
        Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.Equal("committed", receipt.GetProperty("state").GetString());
        Assert.False(receipt.TryGetProperty("id", out _));
        Assert.Equal(1, observed.Writes);
        var recovered = await Command(fixture, ordinary, result.BehaviorKey, "/api/workspace/content/objects/copy/result", new { operationId });
        Assert.True(recovered.GetProperty("succeeded").GetBoolean());
        using (var missingResult = await FeatureHttpTestRequests.SendAsync(ordinary, result, HttpMethod.Post, "/api/workspace/content/objects/copy/result", new { operationId }))
            await Problem(missingResult, HttpStatusCode.BadRequest, "ButtonExecution.BindingInvalid");
        using (var wrongResult = await FeatureHttpTestRequests.SendAsync(ordinary, result, HttpMethod.Post, "/api/workspace/content/objects/copy/result", new { operationId },
            new FeatureHttpTestRequests(fixture).Declaration(copy.BehaviorKey, copy)))
            await Problem(wrongResult, HttpStatusCode.BadRequest, "ButtonExecution.BindingInvalid");

        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, copy, HttpMethod.Post, "/api/workspace/content/objects/copy", retry))
            await Problem(missing, HttpStatusCode.BadRequest, "ButtonExecution.BindingInvalid");
        var borrowed = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.copy", Point(fixture, "workspace.folder.copy"));
        using (var wrong = await FeatureHttpTestRequests.SendAsync(ordinary, copy, HttpMethod.Post, "/api/workspace/content/objects/copy", retry, borrowed))
            await Problem(wrong, HttpStatusCode.BadRequest, "ButtonExecution.BindingInvalid");
        using (var mixed = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/objects/copy") { Content = JsonContent.Create(retry) })
        {
            mixed.Headers.Add("X-NewP-Feature", copy.Position);
            mixed.Headers.Add("X-NewP-Button", new FeatureHttpTestRequests(fixture).Declaration(copy.BehaviorKey, copy).Id);
            mixed.Headers.Add("X-NewP-Data-Declaration", borrowed.Id);
            using var rejected = await ordinary.SendAsync(mixed);
            await Problem(rejected, HttpStatusCode.BadRequest, "ButtonExecution.BindingInvalid");
        }
        using (var foreign = await PartCopySend(fixture, admin, PartCopyDataPhase.Submit, retry))
            await Problem(foreign, HttpStatusCode.NotFound, "Workspace.CopyOperationUnavailable");
        foreach (var (model, expected) in before) Assert.Equal(expected, Snapshot(await Rows(fixture, model)));
        await new FeatureHttpTestRequests(fixture).GrantAsync(admin, actor.RoleId, access);
        var afterGrantAudit = Snapshot(await Rows(fixture, "BusinessChange"));
        using (var revoked = await PartCopySend(fixture, ordinary, PartCopyDataPhase.Submit, retry))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        foreach (var (model, expected) in before.Where(pair => pair.Key != "BusinessChange")) Assert.Equal(expected, Snapshot(await Rows(fixture, model)));
        Assert.Equal(afterGrantAudit, Snapshot(await Rows(fixture, "BusinessChange")));
    }

    private sealed class CopyBoundaryObservation
    {
        internal int Writes;
        internal object? State;
    }

    private sealed class CopyBoundaryRecorder(IButtonExecutionAccess access, CopyBoundaryObservation observed) : IButtonBusinessExecutor
    {
        private readonly ButtonBusinessExecutor actual = new(access);
        public Task<TResult> ExecuteAsync<TInput, TState, TResult>(IButtonBusinessHandler<TInput, TState, TResult> handler,
            TInput input, CancellationToken cancellationToken = default)
        {
            if (input is WorkspaceObjectCopyState<StandardCrudMutationResult>)
            { observed.Writes++; observed.State = input; }
            return actual.ExecuteAsync(handler, input, cancellationToken);
        }
    }

}
