using System.Text.Json;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>声明真实对象创建与文件夹引用的协调范围；不复制目标模块的写入规则。</summary>
public sealed class WorkspaceObjectCreation(Func<WorkspaceContents> resolveContents, Func<IObjectOperationExecutor> operations) : IObjectBehavior
{
    public static readonly ObjectOperationAction CreatePart = new("createPart");
    public static readonly ObjectOperationAction CreateDocument = new("createDocument");
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "workspace.content.creation", "Workspace", "Workspace/WorkspaceFolderContent",
        [CreatePart, CreateDocument], [], ["Position", "ActorId"],
        [new(CreatePart, new("part.part", ObjectOperationAction.Create)),
         new(CreateDocument, new("document.document", ObjectOperationAction.Create))]);
    public ObjectBehaviorDefinition Definition => BehaviorDefinition;

    /// <inheritdoc/>
    public async ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken = default)
    {
        var (resource, model) = context.Action == CreatePart ? ("part.part", "Part/Part")
            : context.Action == CreateDocument ? ("document.document", "Document/Document")
            : throw new InvalidOperationException("Unsupported Workspace creation action.");
        var actor = (string)context.Values["ActorId"]!;
        var position = (WorkspacePosition)context.Values["Position"]!;
        var input = (IReadOnlyDictionary<string, JsonElement>)context.Values["Values"]!;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (field, value) in input)
        {
            if (field is not ("ECODE" or "ENAME" or "Description"))
                throw new LinkQueryException(400, "Workspace.ServerFieldForbidden", field);
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new LinkQueryException(400, "Workspace.InvalidCreateInput", field);
            values[field] = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        }
        var contents = resolveContents();
        var parentId = await contents.PrepareObjectCreationAsync(actor, position, model, cancellationToken);
        var result = await operations().ExecuteAsync(resource, ObjectOperationAction.Create, null, values, cancellationToken);
        if (!result.Succeeded) return result;
        var id = result.Detail?.Fields.GetValueOrDefault("EID") as string
            ?? throw new InvalidOperationException("Created object identity is missing.");
        contents.RegisterCreatedReference(parentId, model, id);
        // 不执行展示读取；事务完成后由独立列表读取决定新对象的可见性。
        return StandardCrudMutationResult.Success();
    }
}
