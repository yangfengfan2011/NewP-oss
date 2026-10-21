using System.Runtime.CompilerServices;
using ICP.Platform.Authorization.Contracts;
using System.Globalization;
using System.Text.Json;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceDocumentCopyItemPreparation(string SourceLinkId, IReadOnlyList<string> SourcePath,
    IReadOnlyDictionary<string, object?> Values, int FileCount, long TotalBytes);
public sealed record WorkspaceDocumentCopyItemReceipt(string SourceLinkId, bool Succeeded, string State);
internal sealed record WorkspaceDocumentCopySource(string LinkId, string Id, string Code, string Name, string? Description, string Confirmation);

public sealed partial class WorkspaceContents
{
    // 同一页已核证LID的请求顺序就是确认顺序；集合不去重、不按EID换路，也不静默筛掉非Document。
    private ValueTask<WorkspaceObjectCopyPreparation> PrepareDocumentBatchAsync(string actor, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, CancellationToken token) => transactions.ExecuteAsync<WorkspaceObjectCopyPreparation>(async cancellation =>
    {
        using var operation = audit.BeginOperation();
        await LockGraphAsync(cancellation);
        var parent = await positions.ResolveAsync(actor, sourcePosition, cancellation);
        var endpoints = await ResolveDocumentCopyTargetsAsync(parent.FolderId, linkIds, "prepare", cancellation);
        if (endpoints.Any(endpoint => endpoint.ModelKey != "Document")) throw new LinkQueryException(400, "Workspace.ObjectCopySourceRequired");
        var target = await ObjectCopyTargetPositionAsync(actor, targetPosition, cancellation);
        RequireContainer(targetPosition, target, "Document");
        var sources = new List<WorkspaceDocumentCopySource>();
        var items = new List<WorkspaceDocumentCopyItemPreparation>();
        // 批量原顺序是全部visibility、目标父、逐来源用途与确认，不能套成单项的先用途后父。
        return await ExecuteCopyPreparationButtonAsync(new ButtonTargetSequence(PrepareSources), checkedToken =>
        {
            var first = sources[0];
            var input = new WorkspaceObjectCopyInput(sourcePosition, linkIds, targetPosition, first.Id, first.Code, first.Name,
                first.Description, "Document", Documents: sources.ToArray());
            var id = ids.NewId("WorkspaceObjectCopy");
            var row = new Dictionary<string, object?> { ["EID"] = id, ["ActorId"] = actor, ["State"] = "ready", ["Input"] = JsonSerializer.Serialize(input) };
            audit.ForCreate().ApplyTo(row); ObjectCopies.Add(row);
            return Task.FromResult<WorkspaceObjectCopyPreparation>(new(id, new Dictionary<string, object?> { ["items"] = items.Select(item => new { item.SourceLinkId, item.Values }).ToArray() },
                parent.Names, target.Names, "Document", items.Sum(item => item.FileCount), items.Sum(item => item.TotalBytes), items.ToArray()));
        }, cancellation);

        // 平台立即检查当前来源后才推进下一行，确认快照仍由Document owner生成。
        async IAsyncEnumerable<ButtonObjectTarget> PrepareSources([EnumeratorCancellation] CancellationToken checkedToken)
        {
            for (var index = 0; index < endpoints.Length; index++)
            {
                yield return ObjectCopySourceTarget(endpoints[index]);
                var source = await documentCopies.ReadCopySourceAsync(endpoints[index].Id, checkedToken);
                sources.Add(new(linkIds[index], endpoints[index].Id, source.Code, source.Name, source.Description, source.Confirmation));
                items.Add(new(linkIds[index], [.. parent.Names, source.Name], new Dictionary<string, object?> {
                    ["ECODE"] = "", ["ENAME"] = source.Name, ["Description"] = source.Description }, source.FileCount, source.TotalBytes));
            }
        }
    }, token);

    // 兼容旧单Document操作的持久输入和文件key；集合的序号来自服务器固定顺序，不能跨行或跨请求借用准备。
    private static WorkspaceDocumentCopySource[] CopySources(WorkspaceObjectCopyInput input) => input.Documents
        ?? [new(input.LinkIds[0], input.SourceId, input.SourceCode, input.SourceName, input.SourceDescription, input.DocumentConfirmation!)];

    // request key仍在Files原有技术长度内；不把文件或批次内容读入内存。
    private static string CopyOperationKey(WorkspaceObjectCopyInput input, string operationId, int index) => input.Documents is null
        ? operationId : operationId + "." + index.ToString(CultureInfo.InvariantCulture);

    // 单对象回执保持原两字段；集合只增加原确认LID对应的安全状态，不公开新对象或隐藏字段。
    private static WorkspaceObjectCopyReceipt CopyReceipt(WorkspaceObjectCopyInput input, string state) => new(state == "committed", state,
        input.Documents?.Select(item => new WorkspaceDocumentCopyItemReceipt(item.LinkId, state == "committed", state)).ToArray());

    // 每行值必须精确对应本操作的真实LID；未知字段、重复JSON成员、调序/缺行/加行均失败关闭。
    private static Dictionary<string, object?>[] ReadCopyValues(WorkspaceObjectCopyInput input, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (input.Documents is null) return [ReadValues(values, "Document")];
        if (values.Count != 1 || !values.TryGetValue("items", out var items) || items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() != input.Documents.Length) throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
        var result = new List<Dictionary<string, object?>>();
        for (var index = 0; index < input.Documents.Length; index++)
        {
            var item = items[index];
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 2
                || !item.TryGetProperty("sourceLinkId", out var link) || link.ValueKind != JsonValueKind.String
                || link.GetString() != input.Documents[index].LinkId || !item.TryGetProperty("values", out var fields)
                || fields.ValueKind != JsonValueKind.Object) throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
            var properties = fields.EnumerateObject().ToArray();
            if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw new LinkQueryException(400, "Workspace.InvalidClipboardInput");
            result.Add(ReadValues(properties.ToDictionary(property => property.Name, property => property.Value), "Document"));
        }
        return result.ToArray();
    }

    // 正常owner错误保持原身份和字段规则，只加已确认来源行定位，不包含不可见对象信息。
    private static StandardCrudError CopyRowError(WorkspaceObjectCopyInput input, int index, StandardCrudError error) => input.Documents is null ? error
        : error with { FieldErrors = error.FieldErrors.Select(field => field with { Field = $"items.{input.Documents[index].LinkId}.{field.Field}" }).ToArray() };

    // 原可见性全批完成后逐项交平台；每项用途允许后才确认当前源快照，后项不能抢占首错。
    private async IAsyncEnumerable<ButtonObjectTarget> PrepareDocumentCopySourceTargetsAsync(string actor, WorkspaceObjectCopyInput input,
        [EnumeratorCancellation] CancellationToken token)
    {
        var parent = await positions.ResolveAsync(actor, input.SourcePosition, token);
        var sources = CopySources(input);
        var endpoints = await ResolveDocumentCopyTargetsAsync(parent.FolderId, input.LinkIds, "submit", token, input.Documents is not null);
        if (endpoints.Length != sources.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        for (var index = 0; index < sources.Length; index++)
        {
            if (endpoints[index].ModelKey != "Document" || endpoints[index].Id != sources[index].Id) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
            yield return ObjectCopySourceTarget(endpoints[index]);
            var current = await documentCopies.ReadCopySourceAsync(sources[index].Id, token);
            if (current.Confirmation != sources[index].Confirmation) throw new LinkQueryException(409, "Workspace.CopySourceChanged");
        }
    }

    // 每次阶段切换及最后提交重新核对全部源，任何一行失效都会拒绝整个集合。
    private async Task RequireDocumentSourcesAsync(string actor, WorkspaceObjectCopyInput input, CancellationToken token)
    {
        var parent = await positions.ResolveAsync(actor, input.SourcePosition, token);
        var sources = CopySources(input);
        var endpoints = await ResolveDocumentCopyTargetsAsync(parent.FolderId, input.LinkIds, "submit", token, input.Documents is not null);
        if (endpoints.Length != sources.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        for (var index = 0; index < sources.Length; index++)
        {
            if (endpoints[index].ModelKey != "Document" || endpoints[index].Id != sources[index].Id) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
            if (!await ObjectCopyAllowedAsync("source", "Document", sources[index].Id, token)) throw new LinkQueryException(403, "Workspace.ObjectCopyDenied");
            var current = await documentCopies.ReadCopySourceAsync(sources[index].Id, token);
            if (current.Confirmation != sources[index].Confirmation) throw new LinkQueryException(409, "Workspace.CopySourceChanged");
        }
    }

    // 唯一外层对象事务已持图锁；逐owner创建只登记本事务，后文档失败会回滚此前所有对象、使用、Link和审计。
    private async ValueTask<StandardCrudMutationResult> CopyDocumentBatchAsync(string actor, string operationId, Dictionary<string, object?> row,
        WorkspaceObjectCopyInput input, IReadOnlyDictionary<string, JsonElement> values, IObjectOperationExecutor operations, CancellationToken token)
    {
        if (!Equals(row["State"], "preparing")) throw new LinkQueryException(409, "Workspace.CopyCancelled");
        var candidates = ReadCopyValues(input, values);
        return await ExecuteCopyButtonAsync(new ButtonTargetSequence(inner => PrepareDocumentCopySourceTargetsAsync(actor, input, inner)), async checkedToken =>
        {
            var parent = await positions.ResolveAsync(actor, input.SourcePosition, checkedToken);
            var target = await ObjectCopyTargetPositionAsync(actor, input.TargetPosition, checkedToken);
            RequireContainer(input.TargetPosition, target, "Document");
            var created = new List<IReadOnlyDictionary<string, object?>>();
            for (var index = 0; index < input.Documents!.Length; index++)
            {
                var source = input.Documents[index];
                var result = await operations.ExecuteAsync("document.document.copy", new("copy"), null,
                    new Dictionary<string, object?> { ["Confirmation"] = source.Confirmation, ["OperationKey"] = CopyOperationKey(input, operationId, index), ["Values"] = candidates[index] }, checkedToken);
                if (!result.Succeeded) return StandardCrudMutationResult.Failure(CopyRowError(input, index, result.Error!));
                var fields = result.Detail!.Fields; var id = (string)fields["EID"]!;
                var link = await RegisterCreatedReferenceAsync(target.FolderId, "Document", id, checkedToken);
                await referenceAudit.ObjectCopiedAsync(source.LinkId, parent.FolderId, new("Document", source.Id), link, target.FolderId, id, checkedToken);
                created.Add(fields);
            }
            // 最后一行的操作也可能触发授权变化；所有来源和新候选要在提交前再次裁决。
            await RequireDocumentSourcesAsync(actor, input, checkedToken);
            await ObjectCopyTargetPositionAsync(actor, input.TargetPosition, checkedToken);
            foreach (var fields in created)
                if (await executionAccess.EvaluateCandidateAsync(DocumentCopyFinalCandidateSource(nameof(CopyDocumentBatchAsync)), "document.document", "candidate", fields, checkedToken)
                    != ICP.Platform.Authorization.Contracts.DataExecutionDecision.Allowed) throw new LinkQueryException(403, "Workspace.ObjectCreateDenied");
            row["State"] = "committed";
            audit.ForUpdate((string)row["CreatedBy"]!, (DateTime)row["CreatedAt"]!).ApplyTo(row);
            return StandardCrudMutationResult.Success();
        }, token);
    }
}
