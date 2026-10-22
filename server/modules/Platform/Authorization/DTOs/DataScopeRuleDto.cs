using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using System.Text.Json.Serialization;

namespace ICP.Platform.Modules.Authorization.DTOs;

/// <summary>根树是保存边界，节点和主体项保留跨保存稳定身份。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeRuleDto(string Id, string RootId, string? ParentId, string Name,
    bool Enabled, string ResourceKey, DataScopeConditionDto Condition,
    IReadOnlyList<DataScopeSubjectDto> Subjects, IReadOnlyList<DataScopeRuleDto> Children);

/// <summary>属性、业务叶子及 AND/OR 组合共享一棵结构化条件树。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeConditionDto(string Type, string Field, string Operator, JsonElement Value, string? ContextKey = null,
    IReadOnlyList<DataScopeConditionDto>? Children = null, string? Key = null);

/// <summary>动作意见使用 allow、deny、unset；目录停用不是第四态。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeSubjectDto(string Id, string Type, string? SubjectId, long Priority,
    IReadOnlyDictionary<string, string> Actions, DataScopeMatchOptions? MatchOptions = null);
