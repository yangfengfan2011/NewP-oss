namespace ICP.Platform.Modules.Authorization.DTOs;

/// <summary>指定用户对既有对象的数据范围裁决；不代替功能入口资格或业务校验。</summary>
public sealed record DataScopeExplanationDto(string ResourceKey, string ObjectId, string AccountId, string AccountName,
    IReadOnlyList<DataScopeActionExplanationDto> Actions);

/// <summary>动作结果与同一次裁决收集的有效来源，读取依赖单独说明。</summary>
public sealed record DataScopeActionExplanationDto(string Key, string Title, bool Allowed, string Reason,
    IReadOnlyList<DataScopeDecisionSourceDto> Sources);

/// <summary>规则路径及主体意见；被后代或对象覆盖的来源保留明确状态。</summary>
public sealed record DataScopeDecisionSourceDto(string Kind, string Id, string Name, string Opinion, string Status,
    IReadOnlyList<DataScopeSubjectDto> Subjects);
