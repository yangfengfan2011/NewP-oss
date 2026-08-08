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
