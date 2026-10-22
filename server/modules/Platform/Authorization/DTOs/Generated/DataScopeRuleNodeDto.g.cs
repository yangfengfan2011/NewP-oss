#nullable enable annotations

namespace ICP.Platform.Modules.Authorization.DTOs;

public sealed class DataScopeRuleNodeDto
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
    public string RootId { get; init; }
    public string? ParentId { get; init; }
    public string Name { get; init; }
    public bool Enabled { get; init; }
    public string ResourceKey { get; init; }
    public string ConditionJson { get; init; }
}
