namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 声明标准 CRUD 资源的唯一约束；这里只表达约束边界，执行流程由运行时服务统一实现。
/// </summary>
public sealed class StandardCrudUniqueConstraint
{
    public required string Key { get; init; }
    public required IReadOnlyList<string> Fields { get; init; }
    public IReadOnlyList<string> ScopeFields { get; init; } = [];
    public bool CaseInsensitive { get; init; } = true;
    public bool TrimWhitespace { get; init; } = true;
    public bool IgnoreWhenAnyFieldMissing { get; init; } = false;
    public required string ErrorCode { get; init; }
    public required string Message { get; init; }
    public required string ReasonKey { get; init; }
}
