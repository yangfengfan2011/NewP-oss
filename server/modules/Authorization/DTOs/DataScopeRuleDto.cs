using System.Text.Json;
using System.Text.Json.Serialization;

namespace ICP.Platform.Modules.Authorization.DTOs;

/// <summary>根树是保存边界，节点和主体项保留跨保存稳定身份。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeRuleDto(string Id, string RootId, string? ParentId, string Name,
    bool Enabled, string ResourceKey, DataScopeConditionDto Condition,
    IReadOnlyList<DataScopeSubjectDto> Subjects, IReadOnlyList<DataScopeRuleDto> Children);

/// <summary>属性条件只指向原始直接属性；本切片只接受 equal 和固定非空值。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeConditionDto(string Type, string Field, string Operator, JsonElement Value);

/// <summary>动作意见使用 allow、deny、unset；目录停用不是第四态。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeSubjectDto(string Id, string Type, string? SubjectId, long Priority,
    IReadOnlyDictionary<string, string> Actions);
