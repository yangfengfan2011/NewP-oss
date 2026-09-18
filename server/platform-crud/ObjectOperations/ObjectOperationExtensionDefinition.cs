namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 声明组件对一个已开放对象动作的扩展目标，以及扩展之间的显式顺序依赖。
/// </summary>
public sealed class ObjectOperationExtensionDefinition
{
    /// <summary>创建不可变扩展声明；运行顺序只由显式依赖和稳定扩展键决定。</summary>
    public ObjectOperationExtensionDefinition(
        string extensionKey,
        string targetModuleId,
        string objectKey,
        ObjectOperationAction action,
        IEnumerable<string>? runsAfter = null,
        IEnumerable<string>? runsBefore = null,
        IEnumerable<ObjectOperationTarget>? calls = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetModuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(action);
        ExtensionKey = extensionKey;
        TargetModuleId = targetModuleId;
        ObjectKey = objectKey;
        Action = action;
        RunsAfter = CopyDependencies(runsAfter);
        RunsBefore = CopyDependencies(runsBefore);
        Calls = calls?.Distinct().ToArray() ?? [];
    }

    /// <summary>扩展点内稳定且唯一的扩展键。</summary>
    public string ExtensionKey { get; }
    /// <summary>拥有目标对象的模块。</summary>
    public string TargetModuleId { get; }
    /// <summary>目标对象稳定键。</summary>
    public string ObjectKey { get; }
    /// <summary>目标整个动作。</summary>
    public ObjectOperationAction Action { get; }
    /// <summary>必须先于本扩展执行的扩展键。</summary>
    public IReadOnlyList<string> RunsAfter { get; }
    /// <summary>必须后于本扩展执行的扩展键。</summary>
    public IReadOnlyList<string> RunsBefore { get; }
    /// <summary>本扩展可能调用的目标对象操作。</summary>
    public IReadOnlyList<ObjectOperationTarget> Calls { get; }

    // 复制并收敛声明，避免模块注册后通过原集合改变启动期计划。
    private static IReadOnlyList<string> CopyDependencies(IEnumerable<string>? values) =>
        values?.Select(static value => value?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
        ?? [];
}
