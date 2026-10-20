using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.LinkQueries;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ICP.Platform.Modules.Workspace;

/// <summary>未迁移真实副本复用的阶段来源适配，不持有复制选择或引用粘贴的按钮合同。</summary>
public static class WorkspaceClipboardExecutions
{
    public const string Provider = "workspace.backend";

    /// <summary>服务端按实际副本来源固定执行、阶段和分支，不从请求缺失推断来源。</summary>
    public static DataExecutionBindingReference Binding(string execution, string stage, string? branch = null) =>
        new(execution, Provider, stage) { Branch = branch };
}

/// <summary>实际 MVC 入口登记执行身份，同时按原 Problem Details 边界报告声明错误。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WorkspaceClipboardExecutionAttribute(string execution, string stage) : Attribute, IAsyncExceptionFilter
{
    public DataExecutionBindingReference Binding { get; } = WorkspaceClipboardExecutions.Binding(execution, stage);

    /// <inheritdoc/>
    public Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is ActionDataAccessException exception)
        {
            context.Result = ActionDataAccessProblem.ToResult(exception, exception.Code);
            context.ExceptionHandled = true;
        }
        return Task.CompletedTask;
    }
}

public sealed partial class WorkspaceContents
{
    // 解析器在统一 read 要求内取得真实父身份与路径事实，后续 edit 不再重复完整读取。
    private async Task<WorkspacePositionPath> ResolveClipboardPositionAsync(DataExecutionBindingReference binding,
        string role, WorkspacePosition position, CancellationToken token)
    {
        WorkspacePositionPath? path = null;
        await foreach (var result in executionAccess.EvaluateObjectsAsync(binding, role, [new("workspace.folder", null, position)], token))
        {
            if (result.Decision == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
            if (result.Decision == DataExecutionDecision.Denied) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
            path = result.Target?.Facts as WorkspacePositionPath ?? throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        }
        return path ?? throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
    }
}
