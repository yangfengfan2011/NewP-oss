using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Runtime;

/// <summary>
/// 在 endpoint 映射前聚合校验 Standard CRUD 权限前缀和操作声明。
/// </summary>
public static class StandardCrudAuthorizationValidator
{
    internal static readonly IReadOnlyList<StandardCrudOperation> MappedEndpointOperations =
    [
        StandardCrudOperation.List,
        StandardCrudOperation.Options,
        StandardCrudOperation.Export,
        StandardCrudOperation.Detail,
        StandardCrudOperation.Create,
        StandardCrudOperation.Update,
        StandardCrudOperation.Disable,
        StandardCrudOperation.Delete
    ];

    /// <summary>
    /// 聚合全部声明错误并以稳定顺序失败，避免修复一个错误后才暴露下一个。
    /// </summary>
    public static void Validate(
        IEnumerable<StandardCrudDescriptor> descriptors,
        IEnumerable<StandardCrudOperation> mappedOperations)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(mappedOperations);

        var descriptorList = descriptors.ToArray();
        var diagnostics = new List<StandardCrudAuthorizationDiagnostic>();

        foreach (var descriptor in descriptorList)
        {
            if (descriptor.Authorization is null
                || (string.IsNullOrWhiteSpace(descriptor.Authorization.PermissionStem) && string.IsNullOrWhiteSpace(descriptor.Authorization.FeatureBehaviorStem)))
            {
                diagnostics.Add(new StandardCrudAuthorizationDiagnostic(
                    StandardCrudAuthorizationFailureCodes.PermissionStemMissing,
                    descriptor.ResourceKey,
                    "Standard CRUD descriptor must declare a Permission Stem."));
            }
        }

        foreach (var operation in mappedOperations.Distinct())
        {
            if (!Enum.IsDefined(operation))
            {
                diagnostics.Add(new StandardCrudAuthorizationDiagnostic(
                    StandardCrudAuthorizationFailureCodes.OperationUnmapped,
                    "*",
                    $"Standard CRUD operation {(int)operation} is not mapped to a permission."));
            }
        }

        foreach (var group in descriptorList.GroupBy(
                     static descriptor => descriptor.ResourceKey,
                     StringComparer.Ordinal))
        {
            if (group.Count() < 2)
            {
                continue;
            }

            var stems = group
                .Select(static descriptor => descriptor.Authorization?.PermissionStem)
                .Where(static stem => !string.IsNullOrWhiteSpace(stem))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var isConflict = stems.Length > 1;
            diagnostics.Add(new StandardCrudAuthorizationDiagnostic(
                isConflict
                    ? StandardCrudAuthorizationFailureCodes.DeclarationConflict
                    : StandardCrudAuthorizationFailureCodes.DeclarationDuplicate,
                group.Key,
                isConflict
                    ? "Standard CRUD resource declares conflicting Permission Stems."
                    : "Standard CRUD resource repeats the same Permission Stem declaration."));
        }

        if (diagnostics.Count == 0)
        {
            return;
        }

        throw new StandardCrudAuthorizationValidationException(
            diagnostics
                .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.ResourceKey, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray());
    }
}
