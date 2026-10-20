using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>人员启停沿既有事务服务执行，不追加对象动作要求。</summary>
public sealed class PersonLifecycleButtonHandler(IPersonLifecycleService service)
    : IButtonBusinessHandler<PersonLifecycleButtonInput, PersonLifecycleButtonInput, PersonLifecycleDto>
{
    /// <inheritdoc/>
    public Task<PersonLifecycleDto> WithinBoundaryAsync(PersonLifecycleButtonInput input, string actionKey,
        Func<PersonLifecycleButtonInput, CancellationToken, Task<PersonLifecycleDto>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(PersonLifecycleButtonInput state) => null;
    /// <inheritdoc/>
    public Task<PersonLifecycleDto> ExecuteAsync(PersonLifecycleButtonInput state, CancellationToken token) =>
        service.SetStatusAsync(state.PersonId, state.Request, state.Actor, token).AsTask();
}
/// <summary>人员启停的正式输入与可信审计主体。</summary>
public sealed record PersonLifecycleButtonInput(string PersonId, SetPersonLifecycleStatusRequest Request, PersonLifecycleActor Actor);

/// <summary>永久绑定只增加按钮身份核验，保留绑定服务原有事务与候选规则。</summary>
public sealed class PersonBindingButtonHandler(IPersonUserBindingService service)
    : IButtonBusinessHandler<PersonBindingButtonInput, PersonBindingButtonInput, PersonUserBindingDto>
{
    /// <inheritdoc/>
    public Task<PersonUserBindingDto> WithinBoundaryAsync(PersonBindingButtonInput input, string actionKey,
        Func<PersonBindingButtonInput, CancellationToken, Task<PersonUserBindingDto>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(PersonBindingButtonInput state) => null;
    /// <inheritdoc/>
    public Task<PersonUserBindingDto> ExecuteAsync(PersonBindingButtonInput state, CancellationToken token) =>
        service.BindAsync(state.PersonId, state.Request, state.Actor, token).AsTask();
}
/// <summary>绑定使用路由人员与正式账号输入，不推导对象动作集合。</summary>
public sealed record PersonBindingButtonInput(string PersonId, BindPersonUserRequest Request, PersonUserBindingActor Actor);

/// <summary>显式无载荷的初始化输入。</summary>
public sealed record OrganizationInitializationButtonInput;
/// <summary>通过有效按钮调用既有业务服务；原事务和业务守卫仍由服务持有。</summary>
public sealed class OrganizationInitializationButtonHandler(OrganizationInitializationService service) : IButtonBusinessHandler<OrganizationInitializationButtonInput, OrganizationInitializationButtonInput, OrganizationInitializationResult>
{
    /// <inheritdoc/>
    public Task<OrganizationInitializationResult> WithinBoundaryAsync(OrganizationInitializationButtonInput input, string actionKey,
        Func<OrganizationInitializationButtonInput, CancellationToken, Task<OrganizationInitializationResult>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(OrganizationInitializationButtonInput state) => null;
    /// <inheritdoc/>
    public Task<OrganizationInitializationResult> ExecuteAsync(OrganizationInitializationButtonInput state, CancellationToken token) =>
        service.InitializeAsync(token).AsTask();
}
