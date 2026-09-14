namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// Standard CRUD 显式声明的权限前缀及其固定操作映射。
/// </summary>
public sealed record StandardCrudAuthorization
{
    private StandardCrudAuthorization(string permissionStem)
    {
        PermissionStem = permissionStem;
    }

    public string PermissionStem { get; }
    public string? FeatureBehaviorStem { get; private init; }
    public IReadOnlySet<StandardCrudOperation> FeatureOperations { get; private init; } = new HashSet<StandardCrudOperation>();

    /// <summary>绑定标准业务实现及实际开放操作；不声明或保存授权位置。</summary>
    public static StandardCrudAuthorization ByFeature(string behaviorStem, params StandardCrudOperation[] operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorStem);
        if (operations.Any(operation => !Enum.IsDefined(operation))) throw new ArgumentException("存在未知标准操作。", nameof(operations));
        return new StandardCrudAuthorization("") { FeatureBehaviorStem = behaviorStem, FeatureOperations = operations.ToHashSet() };
    }

    /// <summary>返回对应标准执行行为；未开放的派生端点保持显式拒绝。</summary>
    public string? GetFeatureBehavior(StandardCrudOperation operation) => !FeatureOperations.Contains(operation) ? null
        : FeatureBehaviorStem + "." + (operation is StandardCrudOperation.List or StandardCrudOperation.Detail ? "read"
            : operation == StandardCrudOperation.Options ? "lookup" : operation.ToString().ToLowerInvariant());

    /// <summary>
    /// 使用平台固定的 View/Create/Update/Delete 约定建立授权声明。
    /// </summary>
    public static StandardCrudAuthorization ByConvention(string permissionStem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionStem);
        return new StandardCrudAuthorization(permissionStem.Trim());
    }

    /// <summary>
    /// 把一个已支持的 Standard CRUD 操作解析为稳定权限代码。
    /// </summary>
    public string GetPermissionCode(StandardCrudOperation operation) => operation switch
    {
        StandardCrudOperation.List or
        StandardCrudOperation.Detail or
        StandardCrudOperation.Options or
        StandardCrudOperation.Export => $"{PermissionStem}.View",
        StandardCrudOperation.Create => $"{PermissionStem}.Create",
        StandardCrudOperation.Update or
        StandardCrudOperation.Disable or
        StandardCrudOperation.Enable => $"{PermissionStem}.Update",
        StandardCrudOperation.Delete => $"{PermissionStem}.Delete",
        _ => throw StandardCrudAuthorizationValidationException.ForUnknownOperation(operation)
    };
}
