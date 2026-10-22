using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Identity.Contracts;

namespace ICP.Platform.Modules.Identity.Application;

/// <summary>账号命令保留既有用例和事务，统一验证调用按钮身份。</summary>
public sealed class IdentityAccountButtonHandler(IIdentityAccountApplicationService service)
    : IButtonBusinessHandler<CreateIdentityAccountCommand, CreateIdentityAccountCommand, IdentityAccountCreationResult>,
    IButtonBusinessHandler<UpdateIdentityAccountNameCommand, UpdateIdentityAccountNameCommand, IdentityAccountSnapshot>,
    IButtonBusinessHandler<SetIdentityAccountStatusCommand, SetIdentityAccountStatusCommand, IdentityAccountSnapshot>,
    IButtonBusinessHandler<DeleteIdentityAccountCommand, DeleteIdentityAccountCommand, IdentityAccountSnapshot>,
    IButtonBusinessHandler<ResetIdentityCredentialCommand, ResetIdentityCredentialCommand, IdentityCredentialResetResult>
{
    /// <inheritdoc/>
    public Task<IdentityAccountCreationResult> WithinBoundaryAsync(CreateIdentityAccountCommand input, string actionKey,
        Func<CreateIdentityAccountCommand, CancellationToken, Task<IdentityAccountCreationResult>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(CreateIdentityAccountCommand state) => null;
    /// <inheritdoc/>
    public Task<IdentityAccountCreationResult> ExecuteAsync(CreateIdentityAccountCommand state, CancellationToken token) => service.CreateAsync(state, token).AsTask();
    /// <inheritdoc/>
    public Task<IdentityAccountSnapshot> WithinBoundaryAsync(UpdateIdentityAccountNameCommand input, string actionKey,
        Func<UpdateIdentityAccountNameCommand, CancellationToken, Task<IdentityAccountSnapshot>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(UpdateIdentityAccountNameCommand state) => null;
    /// <inheritdoc/>
    public Task<IdentityAccountSnapshot> ExecuteAsync(UpdateIdentityAccountNameCommand state, CancellationToken token) => service.UpdateNameAsync(state, token).AsTask();
    /// <inheritdoc/>
    public Task<IdentityAccountSnapshot> WithinBoundaryAsync(SetIdentityAccountStatusCommand input, string actionKey,
        Func<SetIdentityAccountStatusCommand, CancellationToken, Task<IdentityAccountSnapshot>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(SetIdentityAccountStatusCommand state) => null;
    /// <inheritdoc/>
    public Task<IdentityAccountSnapshot> ExecuteAsync(SetIdentityAccountStatusCommand state, CancellationToken token) => service.SetStatusAsync(state, token).AsTask();
    /// <inheritdoc/>
    public Task<IdentityAccountSnapshot> WithinBoundaryAsync(DeleteIdentityAccountCommand input, string actionKey,
        Func<DeleteIdentityAccountCommand, CancellationToken, Task<IdentityAccountSnapshot>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(DeleteIdentityAccountCommand state) => null;
    /// <inheritdoc/>
    public Task<IdentityAccountSnapshot> ExecuteAsync(DeleteIdentityAccountCommand state, CancellationToken token) => service.DeleteAsync(state, token).AsTask();
    /// <inheritdoc/>
    public Task<IdentityCredentialResetResult> WithinBoundaryAsync(ResetIdentityCredentialCommand input, string actionKey,
        Func<ResetIdentityCredentialCommand, CancellationToken, Task<IdentityCredentialResetResult>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(ResetIdentityCredentialCommand state) => null;
    /// <inheritdoc/>
    public Task<IdentityCredentialResetResult> ExecuteAsync(ResetIdentityCredentialCommand state, CancellationToken token) => service.ResetCredentialAsync(state, token).AsTask();
}
