namespace ICP.Platform.Modules.Authorization.DTOs;

/// <summary>
/// 表示面向用户角色分配接口的手写 DTO；基础 UserRoleDto 已由模型生成，避免继续混用两种契约语义。
/// </summary>
public record UserRoleAssignmentsDto
{
    public required string UserId { get; init; }
    public required List<string> RoleCodes { get; init; }
}
