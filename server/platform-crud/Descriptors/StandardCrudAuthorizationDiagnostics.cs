namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// Standard CRUD 授权声明启动校验的稳定失败码。
/// </summary>
public static class StandardCrudAuthorizationFailureCodes
{
    public const string PermissionStemMissing = "AUTH_CRUD_PERMISSION_STEM_MISSING";
    public const string OperationUnmapped = "AUTH_CRUD_OPERATION_UNMAPPED";
    public const string DeclarationDuplicate = "AUTH_CRUD_PERMISSION_DECLARATION_DUPLICATE";
    public const string DeclarationConflict = "AUTH_CRUD_PERMISSION_DECLARATION_CONFLICT";
}

/// <summary>
/// 单个 Standard CRUD 授权声明诊断。
/// </summary>
public sealed record StandardCrudAuthorizationDiagnostic(
    string Code,
    string ResourceKey,
    string Message);

/// <summary>
/// 表示启动阶段收集到一个或多个 Standard CRUD 授权声明错误。
/// </summary>
public sealed class StandardCrudAuthorizationValidationException : InvalidOperationException
{
    public StandardCrudAuthorizationValidationException(
        IReadOnlyList<StandardCrudAuthorizationDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            throw new ArgumentException("Authorization diagnostics cannot be empty.", nameof(diagnostics));
        }

        Diagnostics = diagnostics.ToArray();
    }

    public IReadOnlyList<StandardCrudAuthorizationDiagnostic> Diagnostics { get; }

    internal static StandardCrudAuthorizationValidationException ForUnknownOperation(
        StandardCrudOperation operation) =>
        new(
        [
            new StandardCrudAuthorizationDiagnostic(
                StandardCrudAuthorizationFailureCodes.OperationUnmapped,
                "*",
                $"Standard CRUD operation {(int)operation} is not mapped to a permission.")
        ]);

    private static string BuildMessage(IReadOnlyList<StandardCrudAuthorizationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.ResourceKey}: {diagnostic.Message}"));
    }
}
