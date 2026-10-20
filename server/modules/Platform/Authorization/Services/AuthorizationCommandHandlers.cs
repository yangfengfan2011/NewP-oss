using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>创建角色的正式字段与可信主体名称。</summary>
public sealed record RoleCreateButtonInput(RoleDto Input, string? Actor);
/// <summary>更新的规范目标来自路由，载荷不得另行选择目标。</summary>
public sealed record RoleUpdateButtonInput(string RoleId, RoleDto Input, string? Actor);
/// <summary>账号角色以完整集合替换。</summary>
public sealed record UserRolesButtonInput(string AccountId, IReadOnlyCollection<string> Codes);
/// <summary>角色功能以完整位置集合替换。</summary>
public sealed record FeatureGrantsButtonInput(string RoleId, IReadOnlyCollection<string> Positions);

/// <summary>通过有效按钮调用既有业务服务；原事务和业务守卫仍由服务持有。</summary>
public sealed class RoleCreateButtonHandler(IRoleService service) : IButtonBusinessHandler<RoleCreateButtonInput, RoleCreateButtonInput, RoleDto>
{
    /// <inheritdoc/>
    public Task<RoleDto> WithinBoundaryAsync(RoleCreateButtonInput input, string actionKey,
        Func<RoleCreateButtonInput, CancellationToken, Task<RoleDto>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(RoleCreateButtonInput state) => null;
    /// <inheritdoc/>
    public Task<RoleDto> ExecuteAsync(RoleCreateButtonInput state, CancellationToken token) =>
        service.CreateRoleAsync(state.Input, state.Actor, token);
}
/// <summary>通过有效按钮调用既有业务服务；原事务和业务守卫仍由服务持有。</summary>
public sealed class RoleUpdateButtonHandler(IRoleService service) : IButtonBusinessHandler<RoleUpdateButtonInput, RoleUpdateButtonInput, RoleDto>
{
    /// <inheritdoc/>
    public Task<RoleDto> WithinBoundaryAsync(RoleUpdateButtonInput input, string actionKey,
        Func<RoleUpdateButtonInput, CancellationToken, Task<RoleDto>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(RoleUpdateButtonInput state) => null;
    /// <inheritdoc/>
    public Task<RoleDto> ExecuteAsync(RoleUpdateButtonInput state, CancellationToken token) =>
        service.UpdateRoleAsync(state.RoleId, state.Input, state.Actor, token);
}
/// <summary>通过有效按钮调用既有业务服务；原事务和业务守卫仍由服务持有。</summary>
public sealed class UserRolesButtonHandler(IUserRoleAssignments service) : IButtonBusinessHandler<UserRolesButtonInput, UserRolesButtonInput, bool>
{
    /// <inheritdoc/>
    public Task<bool> WithinBoundaryAsync(UserRolesButtonInput input, string actionKey,
        Func<UserRolesButtonInput, CancellationToken, Task<bool>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(UserRolesButtonInput state) => null;
    /// <inheritdoc/>
    public async Task<bool> ExecuteAsync(UserRolesButtonInput state, CancellationToken token) {
        await service.ReplaceAsync(state.AccountId, state.Codes, token);
        return true;
    }
}
/// <summary>通过有效按钮调用既有业务服务；原事务和业务守卫仍由服务持有。</summary>
public sealed class FeatureGrantsButtonHandler(RoleFeatureAssignments service) : IButtonBusinessHandler<FeatureGrantsButtonInput, FeatureGrantsButtonInput, bool>
{
    /// <inheritdoc/>
    public Task<bool> WithinBoundaryAsync(FeatureGrantsButtonInput input, string actionKey,
        Func<FeatureGrantsButtonInput, CancellationToken, Task<bool>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(FeatureGrantsButtonInput state) => null;
    /// <inheritdoc/>
    public async Task<bool> ExecuteAsync(FeatureGrantsButtonInput state, CancellationToken token) {
        await service.ReplaceAsync(state.RoleId, state.Positions, token);
        return true;
    }
}
/// <summary>通过有效按钮调用既有业务服务；原事务和业务守卫仍由服务持有。</summary>
public sealed class RuleSaveButtonHandler(DataScopeRules service) : IButtonBusinessHandler<DataScopeRuleDto, DataScopeRuleDto, bool>
{
    /// <inheritdoc/>
    public Task<bool> WithinBoundaryAsync(DataScopeRuleDto input, string actionKey,
        Func<DataScopeRuleDto, CancellationToken, Task<bool>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(DataScopeRuleDto state) => null;
    /// <inheritdoc/>
    public async Task<bool> ExecuteAsync(DataScopeRuleDto state, CancellationToken token) {
        await service.SaveAsync(state, token);
        return true;
    }
}
/// <summary>通过有效按钮调用既有业务服务；原事务和业务守卫仍由服务持有。</summary>
public sealed class RuleDeleteButtonHandler(DataScopeRules service) : IButtonBusinessHandler<string, string, bool>
{
    /// <inheritdoc/>
    public Task<bool> WithinBoundaryAsync(string input, string actionKey,
        Func<string, CancellationToken, Task<bool>> execute, CancellationToken token) => execute(input, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget? Target(string state) => null;
    /// <inheritdoc/>
    public async Task<bool> ExecuteAsync(string state, CancellationToken token) {
        await service.DeleteAsync(state, token);
        return true;
    }
}
