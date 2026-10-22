using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Document;

/// <summary>正式履历输入区分对象、准备操作、记录与输出范围。</summary>
public sealed record DocumentHistoryButtonInput(string ObjectId, string Operation, ObjectHistoryFilter Filter,
    string? RecordId = null, ObjectHistoryOutputRequest? Output = null);
/// <summary>平台检查与安全查询使用同一实际对象。</summary>
public sealed record DocumentHistoryButtonState(DocumentHistoryButtonInput Input, IReadOnlyDictionary<string, object?> Before);

/// <summary>Document 履历提供真实对象边界，入口 read 由平台按当前按钮唯一声明检查。</summary>
public sealed class DocumentHistoryButtonHandler(IStandardCrudResourceRegistry resources, IStandardCrudRepository repository,
    IObjectHistoryQuery history, IObjectHistoryExport export)
    : IButtonBusinessHandler<DocumentHistoryButtonInput, DocumentHistoryButtonState, IResult>
{
    /// <inheritdoc/>
    public async Task<IResult> WithinBoundaryAsync(DocumentHistoryButtonInput input, string actionKey,
        Func<DocumentHistoryButtonState, CancellationToken, Task<IResult>> execute, CancellationToken cancellationToken)
    {
        var expected = input.Operation switch
        {
            "enter" => actionKey is "history" or "document.object.history",
            "preview" => actionKey == "output",
            "detail" => actionKey == "view",
            "export" or "print" => actionKey == input.Operation + (input.Output?.Mode == "selected" ? "Selected" : "All"),
            _ => false
        };
        if (!expected) throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        var before = await repository.GetByIdAsync(resources.FindByResourceKey("document.document")!, input.ObjectId, cancellationToken)
            ?? throw new ActionDataAccessException(404, "ObjectHistory.Unavailable");
        return await execute(new(input, before), cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DocumentHistoryButtonState state) => new ButtonExecutionTarget("document.document", "Document", state.Input.ObjectId, state.Before);

    /// <inheritdoc/>
    public async Task<IResult> ExecuteAsync(DocumentHistoryButtonState state, CancellationToken cancellationToken)
    {
        var input = state.Input;
        if (input.Operation is "enter" or "preview" || input.Operation == "detail" && input.RecordId is null) return Results.NoContent();
        if (input.Operation == "export")
        {
            var file = await export.ExportAsync("document.document", input.ObjectId, input.Output!, cancellationToken);
            return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
        }
        if (input.Operation == "print")
        {
            var items = await history.OutputAsync("document.document", input.ObjectId, input.Output!, cancellationToken);
            return items is null ? Results.NotFound() : Results.Ok(new { items });
        }
        var item = await history.GetAsync("document.document", input.ObjectId, input.RecordId!, input.Filter, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }
}
