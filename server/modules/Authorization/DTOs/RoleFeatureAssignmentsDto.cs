namespace ICP.Platform.Modules.Authorization.DTOs;

/// <summary>目标角色及其当前活动目录中的完整功能位置集合。</summary>
public sealed record RoleFeatureAssignmentsDto(RoleDto Role, IReadOnlyList<string> Positions);
