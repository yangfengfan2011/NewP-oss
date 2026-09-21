namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>角色维护的确定性业务拒绝；持久化、审计及其他依赖故障不属于此类型。</summary>
public sealed class RoleValidationException(string message) : InvalidOperationException(message);
