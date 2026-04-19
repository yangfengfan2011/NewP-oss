namespace ICP.Platform.Modules.Organization.DTOs;

public sealed record PersonSummaryBlockDto
{
    public required string Status { get; init; }

    // 摘要块只承载状态化结果，Data 允许保持最小只读对象，避免把交互逻辑下沉到 DTO。
    public object? Data { get; init; }
}
