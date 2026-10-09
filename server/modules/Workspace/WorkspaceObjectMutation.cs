using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>位置内的对象编辑和混合批量删除；只写 Workspace 自有实体，跨模块写入调用所属行为。</summary>
public sealed class WorkspaceObjectMutation(EfPlatformDbContext db, Func<WorkspaceContents> contents,
    Func<LinkQueryReader> reader, Func<IStandardCrudService> crud, IStandardCrudResourceRegistry resources,
    IDataScopeObjectDecisionProvider decisions, IEntityAuditValueProvider audit, Func<IObjectOperationExecutor> operations) : IObjectBehavior
{
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "workspace.content.mutation", "Workspace", WorkspacePositions.ContentModel,
        [ObjectOperationAction.Update, ObjectOperationAction.Delete], [], ["ActorId", "Position", "LinkIds"],
        [new(ObjectOperationAction.Update, new("part.part", ObjectOperationAction.Update)),
         new(ObjectOperationAction.Update, new("document.document", ObjectOperationAction.Update)),
         new(ObjectOperationAction.Delete, new("part.part", ObjectOperationAction.Delete)),
         new(ObjectOperationAction.Delete, new("document.document", ObjectOperationAction.Delete))]);
    public ObjectBehaviorDefinition Definition => BehaviorDefinition;

    /// <inheritdoc/>
    public async ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken = default)
    {
        var actor = (string)context.Values["ActorId"]!;
        var position = (WorkspacePosition)context.Values["Position"]!;
        var linkIds = (string[])context.Values["LinkIds"]!;
        var editing = context.Action == ObjectOperationAction.Update;
        ValidateSelection(linkIds);
        if (editing && linkIds.Length != 1) throw new LinkQueryException(400, "Workspace.InvalidSelection");
        // 与创建、引用变更取得同一图锁；等待之后重新验证整个位置和对象资格。
        await contents().PrepareObjectWriteAsync(actor, position, WorkspacePositions.FolderModel, cancellationToken);
        var targets = await ResolveTargetsAsync(actor, position, linkIds, editing ? "edit" : "delete", cancellationToken);
        if (!editing)
        {
            var folders = targets.Where(target => target.ModelKey == WorkspacePositions.FolderModel).Select(target => target.Id).ToArray();
            // 在任何引用被清理前检查整批文件夹，避免选择的其他对象使原本非空的文件夹变空。
            if (folders.Length > 0 && await Links.AnyAsync(row => folders.Contains(EF.Property<string>(row, "PEID")), cancellationToken))
                throw new LinkQueryException(409, "Workspace.FolderNotEmpty");
        }
        foreach (var target in targets)
        {
            if (editing)
            {
                var values = ReadValues((IReadOnlyDictionary<string, JsonElement>)context.Values["Values"]!, target.ModelKey);
                if (target.ModelKey == WorkspacePositions.FolderModel) await UpdateFolderAsync(target.Id, values, cancellationToken);
                else
                {
                    var result = await operations().ExecuteAsync(Resource(target.ModelKey), ObjectOperationAction.Update, target.Id, values, cancellationToken);
                    if (!result.Succeeded) return result;
                }
            }
            else
            {
                // 清理全部入向关系留在 SQL 内；后续对象失败时同一事务回滚，包括先前成功的批量项。
                await Links.Where(row => EF.Property<string>(row, "ChildModelKey") == target.ModelKey && EF.Property<string>(row, "CEID") == target.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                if (target.ModelKey == WorkspacePositions.FolderModel)
                    await Folders.Where(row => EF.Property<string>(row, "EID") == target.Id).ExecuteDeleteAsync(cancellationToken);
                else
                {
                    var result = await operations().ExecuteAsync(Resource(target.ModelKey), ObjectOperationAction.Delete, target.Id, new Dictionary<string, object?>(), cancellationToken);
                    if (!result.Succeeded) return result;
                }
            }
        }
        return StandardCrudMutationResult.Success();
    }

    /// <summary>编辑装载使用同一位置和 edit 裁决；只返回业务可编辑字段，不泄露来源或服务器维护字段。</summary>
    public async Task<IReadOnlyDictionary<string, object?>> ReadEditValuesAsync(string actor, WorkspacePosition position, string linkId, CancellationToken token)
    {
        var target = (await ResolveTargetsAsync(actor, position, [linkId], "edit", token)).Single();
        IReadOnlyDictionary<string, object?> fields;
        if (target.ModelKey == WorkspacePositions.FolderModel)
            fields = await Folders.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == target.Id, token);
        else
            fields = (await crud().GetDetailAsync(resources.FindByResourceKey(Resource(target.ModelKey))!, target.Id, token))?.Fields
                ?? throw new LinkQueryException(404, "Workspace.TargetUnavailable");
        string[] names = target.ModelKey == WorkspacePositions.FolderModel ? ["ENAME", "Description"] : ["ECODE", "ENAME", "Description"];
        return names.ToDictionary(name => name, name => fields.GetValueOrDefault(name));
    }

    private DbSet<Dictionary<string, object?>> Links => db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
    private DbSet<Dictionary<string, object?>> Folders => db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel);

    // 先批量匹配当前父端的 LID，再对最多一页对象重验真实端点与动作权限；客户端不能提交类型/EID 替换目标。
    private async Task<LinkObjectReference[]> ResolveTargetsAsync(string actor, WorkspacePosition position, string[] linkIds, string action, CancellationToken token)
    {
        ValidateSelection(linkIds);
        var path = await contents().WritablePositionAsync(actor, position, token);
        var rows = await Links.AsNoTracking().Where(row => linkIds.Contains(EF.Property<string>(row, "LID")) && EF.Property<string>(row, "PEID") == path.FolderId)
            .Select(row => new { Model = EF.Property<string>(row, "ChildModelKey"), Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
        if (rows.Length != linkIds.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        var targets = rows.Select(row => new LinkObjectReference(row.Model, row.Id)).Distinct().ToArray();
        foreach (var target in targets)
        {
            var resource = Resource(target.ModelKey);
            if (!await reader().IsEndpointReadableAsync("workspace.content", target, token)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            if (!await decisions.IsAllowedAsync(resource, target.Id, action, token)) throw new LinkQueryException(403, "Workspace.ObjectWriteDenied");
            if (target.ModelKey == WorkspacePositions.FolderModel && await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceSpace")
                .AnyAsync(row => EF.Property<string>(row, "RootFolderId") == target.Id, token)) throw new LinkQueryException(409, "Workspace.RootMutationForbidden");
        }
        return targets;
    }

    // 每个请求最多消费标准列表一页；不接受空、重复或无界选择。
    private static void ValidateSelection(string[] ids)
    {
        if (ids.Length is < 1 or > 100 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
            throw new LinkQueryException(400, "Workspace.InvalidSelection");
    }

    // 静态允许的类型与行为对应关系属于 Workspace；未知端点失败关闭。
    private static string Resource(string model) => model switch
    {
        WorkspacePositions.FolderModel => "workspace.folder", "Part/Part" => "part.part", "Document/Document" => "document.document",
        _ => throw new LinkQueryException(400, "Workspace.InvalidEndpoint")
    };

    // 请求字段白名单在进入目标行为前检查；所属模块仍执行规范化、长度和唯一性规则。
    private static Dictionary<string, object?> ReadValues(IReadOnlyDictionary<string, JsonElement> input, string model)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in input)
        {
            if (key is not ("ENAME" or "Description") && (key != "ECODE" || model == WorkspacePositions.FolderModel))
                throw new LinkQueryException(400, "Workspace.ServerFieldForbidden", key);
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new LinkQueryException(400, "Workspace.InvalidEditInput", key);
            values[key] = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        }
        return values;
    }

    // 同名约束作用于所有真实父文件夹；只检查是否冲突，不物化或返回不可见路径。
    private async Task UpdateFolderAsync(string id, IReadOnlyDictionary<string, object?> values, CancellationToken token)
    {
        var folder = await Folders.SingleAsync(row => EF.Property<string>(row, "EID") == id, token);
        if (values.TryGetValue("ENAME", out var name))
        {
            if (name is not string text || string.IsNullOrWhiteSpace(text) || text.Length > 256) throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
            if (await Links.AnyAsync(sibling => EF.Property<string>(sibling, "ChildModelKey") == WorkspacePositions.FolderModel
                && EF.Property<string>(sibling, "CEID") != id
                && Links.Any(parent => EF.Property<string>(parent, "ChildModelKey") == WorkspacePositions.FolderModel && EF.Property<string>(parent, "CEID") == id
                    && EF.Property<string>(parent, "PEID") == EF.Property<string>(sibling, "PEID"))
                && Folders.Any(other => EF.Property<string>(other, "EID") == EF.Property<string>(sibling, "CEID") && EF.Property<string>(other, "ENAME") == text), token))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
        }
        if (values.GetValueOrDefault("Description") is string { Length: > 500 }) throw new LinkQueryException(400, "Workspace.DescriptionTooLong", "Description");
        foreach (var (key, value) in values) folder[key] = value;
        audit.ForUpdate((string)folder["CreatedBy"]!, (DateTime)folder["CreatedAt"]!).ApplyTo(folder);
    }
}
