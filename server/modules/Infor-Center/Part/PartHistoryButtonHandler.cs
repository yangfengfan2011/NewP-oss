using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Part;

/// <summary>正式履历输入区分对象、准备操作、记录与输出范围。</summary>
public sealed record PartHistoryButtonInput(string ObjectId, string Operation, ObjectHistoryFilter Filter,
    string? RecordId = null, ObjectHistoryOutputRequest? Output = null);
/// <summary>平台检查与安全查询使用同一实际对象。</summary>
public sealed record PartHistoryButtonState(PartHistoryButtonInput Input, IReadOnlyDictionary<string, object?> Before);

/// <summary>Part 履历提供真实对象边界，入口 read 由平台按当前按钮唯一声明检查。</summary>
public sealed class PartHistoryButtonHandler(IStandardCrudResourceRegistry resources, IStandardCrudRepository repository,
    IObjectHistoryQuery history, IObjectHistoryExport export)
    : IButtonBusinessHandler<PartHistoryButtonInput, PartHistoryButtonState, IResult>
{
    /// <inheritdoc/>
    public async Task<IResult> WithinBoundaryAsync(PartHistoryButtonInput input, string actionKey,
        Func<PartHistoryButtonState, CancellationToken, Task<IResult>> execute, CancellationToken cancellationToken)
    {
        var expected = input.Operation switch
        {
            "enter" => actionKey is "history" or "part.object.history",
            "preview" => actionKey == "output",
            "detail" => actionKey == "view",
            "export" or "print" => actionKey == input.Operation + (input.Output?.Mode == "selected" ? "Selected" : "All"),
            _ => false
        };
        if (!expected) throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        var before = await repository.GetByIdAsync(resources.FindByResourceKey("part.part")!, input.ObjectId, cancellationToken)
            ?? throw new ActionDataAccessException(404, "ObjectHistory.Unavailable");
        return await execute(new(input, before), cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(PartHistoryButtonState state) => new ButtonExecutionTarget("part.part", "Part", state.Input.ObjectId, state.Before);

    /// <inheritdoc/>
    public async Task<IResult> ExecuteAsync(PartHistoryButtonState state, CancellationToken cancellationToken)
    {
        var input = state.Input;
        if (input.Operation is "enter" or "preview" || input.Operation == "detail" && input.RecordId is null) return Results.NoContent();
        if (input.Operation == "export")
        {
            var file = await export.ExportAsync("part.part", input.ObjectId, input.Output!, cancellationToken);
            return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
        }
        if (input.Operation == "print")
        {
            var items = await history.OutputAsync("part.part", input.ObjectId, input.Output!, cancellationToken);
            return items is null ? Results.NotFound() : Results.Ok(new { items });
        }
        var item = await history.GetAsync("part.part", input.ObjectId, input.RecordId!, input.Filter, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }
}
