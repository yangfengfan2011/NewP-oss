using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Document;

/// <summary>Document所属复制行为拥有初始身份和完整文件集，不向Workspace公开私表操作。</summary>
public sealed class DocumentCopyBehavior(DocumentFiles files) : IObjectBehavior
{
    public static readonly ObjectOperationAction Copy = new("copy");
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new("document.document.copy", "Document", "Document", [Copy], [],
        ["Confirmation", "OperationKey"], [new(Copy, new("document.document", ObjectOperationAction.Create))]);
    public ObjectBehaviorDefinition Definition => BehaviorDefinition;

    /// <inheritdoc/>
    public ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken = default) =>
        files.CopyAsync((string)context.Values["Confirmation"]!, (string)context.Values["OperationKey"]!,
            (IReadOnlyDictionary<string, object?>)context.Values["Values"]!, cancellationToken);
}
