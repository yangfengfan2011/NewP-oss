using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Workspace;

/// <summary>履历沿实际出现位置和LID定位目标；客户端不能另给权限对象。</summary>
public sealed record WorkspaceHistoryButtonInput(string ActorId, WorkspacePosition Position, string LinkId, string Operation,
    ObjectHistoryFilter Filter, string? RecordId = null, ObjectHistoryOutputRequest? Output = null);

/// <summary>来源解析后的真实对象与读取前事实，供按钮检查和履历输出共用。</summary>
public sealed record WorkspaceHistoryButtonState(WorkspaceHistoryButtonInput Input, string ResourceKey, string ObjectName,
    string ObjectId, IReadOnlyDictionary<string, object?> Before);

/// <summary>保留Workspace完整来源解析，平台按本次实际对象匹配按钮中的唯一类型要求。</summary>
public sealed class WorkspaceHistoryButtonHandler(WorkspaceContents contents, IStandardCrudResourceRegistry resources,
    WorkspaceFolderReadDescriptor folder, IStandardCrudRepository repository, IObjectHistoryQuery history, IObjectHistoryExport export)
    : IButtonBusinessHandler<WorkspaceHistoryButtonInput, WorkspaceHistoryButtonState, IResult>
{
    /// <inheritdoc/>
    public async Task<IResult> WithinBoundaryAsync(WorkspaceHistoryButtonInput input, string actionKey,
        Func<WorkspaceHistoryButtonState, CancellationToken, Task<IResult>> execute, CancellationToken cancellationToken)
    {
        var expected = input.Operation switch
        {
            "enter" => actionKey == "history",
            "preview" => actionKey == "output",
            "detail" => actionKey == "view",
            "export" or "print" => actionKey == input.Operation + (input.Output?.Mode == "selected" ? "Selected" : "All"),
            _ => false
        };
        if (!expected) throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        var target = await contents.ResolvePermissionTargetAsync(input.ActorId, input.Position, input.LinkId, cancellationToken);
        var descriptor = target.ResourceKey == folder.Value.ResourceKey ? folder.Value : resources.FindByResourceKey(target.ResourceKey)
            ?? throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
        var before = await repository.GetByIdAsync(descriptor, target.ObjectId, cancellationToken)
            ?? throw new ActionDataAccessException(404, "ObjectHistory.Unavailable");
        return await execute(new(input, target.ResourceKey, descriptor.ModelName, target.ObjectId, before), cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceHistoryButtonState state) =>
        new ButtonExecutionTarget(state.ResourceKey, state.ObjectName, state.ObjectId, state.Before);

    /// <inheritdoc/>
    public async Task<IResult> ExecuteAsync(WorkspaceHistoryButtonState state, CancellationToken cancellationToken)
    {
        var input = state.Input;
        if (input.Operation is "enter" or "preview" || input.Operation == "detail" && input.RecordId is null) return Results.NoContent();
        if (input.Operation == "export")
        {
            var file = await export.ExportAsync(state.ResourceKey, state.ObjectId, input.Output!, cancellationToken);
            return file is null ? Results.NotFound() : Results.File(file.Content, file.ContentType, file.FileName);
        }
        if (input.Operation == "print")
        {
            var items = await history.OutputAsync(state.ResourceKey, state.ObjectId, input.Output!, cancellationToken);
            return items is null ? Results.NotFound() : Results.Ok(new { items });
        }
        var item = await history.GetAsync(state.ResourceKey, state.ObjectId, input.RecordId!, input.Filter, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }
}
