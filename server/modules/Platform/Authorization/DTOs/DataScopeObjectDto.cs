using System.Text.Json.Serialization;

namespace ICP.Platform.Modules.Authorization.DTOs;

/// <summary>一个规范资源对象的完整授权配置；主体项沿用规则授权的身份和动作三态。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeObjectDto(string ResourceKey, string ObjectId, IReadOnlyList<DataScopeSubjectDto> Subjects);
