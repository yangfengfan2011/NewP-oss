using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>业务拥有保护生命周期，平台拥有从同一规范目标检查到实际执行的唯一衔接。</summary>
public sealed class ButtonBusinessExecutor(IButtonExecutionAccess access) : IButtonBusinessExecutor
{
    /// <inheritdoc/>
    public async Task<TResult> ExecuteAsync<TInput, TState, TResult>(IButtonBusinessHandler<TInput, TState, TResult> handler,
        TInput input, CancellationToken cancellationToken = default)
    {
        var invocation = access.BindHandler(handler.GetType());
        var calls = 0;
        var result = await handler.WithinBoundaryAsync(input, invocation.ActionKey, async (state, token) =>
        {
            if (Interlocked.Increment(ref calls) != 1) throw new InvalidOperationException("BUTTON_BUSINESS_BOUNDARY_REUSED");
            return await access.ExecuteAsync(invocation, handler.Target(state), inner => handler.ExecuteAsync(state, inner), token);
        }, cancellationToken);
        if (calls != 1) throw new InvalidOperationException("BUTTON_BUSINESS_BOUNDARY_REQUIRED");
        return result;
    }
}
