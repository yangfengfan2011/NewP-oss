#nullable enable annotations

namespace ICP.Platform.Modules.Authorization.DTOs;

public sealed class DataScopeRuleSubjectDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public string? CreatedBy_DisplayValue { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedAt_DisplayValue { get; init; }
    public string UpdatedBy { get; init; }
    public string? UpdatedBy_DisplayValue { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? UpdatedAt_DisplayValue { get; init; }
    public string? MatchOptionsJson { get; init; }
    public string NodeId { get; init; }
    public string SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public int Priority { get; init; }
    public string ActionsJson { get; init; }
}
