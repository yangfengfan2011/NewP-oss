using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Runtime;

/// <summary>标准导出的正式资源和查询输入；不接收权限专用对象ID。</summary>
public sealed record StandardCrudExportInput(StandardCrudDescriptor Descriptor, StandardCrudExportRequest Request);

/// <summary>按钮与实际导出共享的原查询来源，不承载允许结果。</summary>
public sealed record StandardCrudExportState(StandardCrudExportInput Input, ButtonQueryTarget Target);

/// <summary>在当前按钮调用范围内启用原SQL查询来源，过滤、排序、分页和上限仍由原导出服务处理。</summary>
public sealed class StandardCrudExportHandler(StandardCrudExportService export)
    : IButtonBusinessHandler<StandardCrudExportInput, StandardCrudExportState, StandardCrudExportFile>
{
    /// <inheritdoc/>
    public Task<StandardCrudExportFile> WithinBoundaryAsync(StandardCrudExportInput input, string actionKey,
        Func<StandardCrudExportState, CancellationToken, Task<StandardCrudExportFile>> execute, CancellationToken cancellationToken)
    {
        if (actionKey != "export" || !input.Descriptor.DataExecutions.TryGetValue("export", out var execution))
            throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        var target = new ButtonQueryTarget(input.Descriptor.ResourceKey, input.Descriptor.ModelName,
            new(new(execution, "standardCrud.export", "read"), "query"));
        return execute(new(input, target), cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(StandardCrudExportState state) => state.Target;

    /// <inheritdoc/>
    public Task<StandardCrudExportFile> ExecuteAsync(StandardCrudExportState state, CancellationToken cancellationToken) =>
        export.ExportAsync(state.Input.Descriptor, state.Input.Request, cancellationToken);
}
