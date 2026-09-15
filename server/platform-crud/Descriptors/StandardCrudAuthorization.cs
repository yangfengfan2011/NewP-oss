namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// Standard CRUD 显式声明的功能行为前缀及其固定操作映射。
/// </summary>
public sealed record StandardCrudAuthorization
{
    private StandardCrudAuthorization(string behaviorStem)
    {
        FeatureBehaviorStem = behaviorStem;
    }

    public string FeatureBehaviorStem { get; }
    public IReadOnlySet<StandardCrudOperation> FeatureOperations { get; private init; } = new HashSet<StandardCrudOperation>();

    /// <summary>绑定标准业务实现及实际开放操作；不声明或保存授权位置。</summary>
    public static StandardCrudAuthorization ByFeature(string behaviorStem, params StandardCrudOperation[] operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorStem);
        if (operations.Any(operation => !Enum.IsDefined(operation))) throw new ArgumentException("存在未知标准操作。", nameof(operations));
        return new StandardCrudAuthorization(behaviorStem) { FeatureOperations = operations.ToHashSet() };
    }

    /// <summary>返回对应标准执行行为；未开放的派生端点保持显式拒绝。</summary>
    public string? GetFeatureBehavior(StandardCrudOperation operation) => !FeatureOperations.Contains(operation) ? null
        : FeatureBehaviorStem + "." + (operation is StandardCrudOperation.List or StandardCrudOperation.Detail ? "read"
            : operation == StandardCrudOperation.Options ? "lookup" : operation.ToString().ToLowerInvariant());

}
