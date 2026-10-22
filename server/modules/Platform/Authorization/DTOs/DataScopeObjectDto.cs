using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.DTOs;

/// <summary>一个规范资源对象的完整授权配置；直接项拥有独立期限，规则主体不接受此字段。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeObjectDto(string ResourceKey, string ObjectId, IReadOnlyList<DataScopeDirectSubjectDto> Subjects)
    : DataScopeObjectConfiguration(ResourceKey, ObjectId, Subjects);
