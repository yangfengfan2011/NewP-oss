namespace ICP.Platform.Modules.Organization.Models;

/// <summary>
/// Person 聚合中的账号绑定事实投影；它不是独立实体，也不拥有 EID/LID。
/// </summary>
public sealed record PersonUserBindingState
{
    public required string PersonEid { get; init; }
    public required string PersonName { get; init; }
    public bool IsEnabled { get; init; }
    public long Revision { get; init; }
    public string? UserEid { get; init; }
    public DateTime? BoundAt { get; init; }
    public string? BoundBy { get; init; }
    public string? BindingReason { get; init; }
    public string? UpdatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
