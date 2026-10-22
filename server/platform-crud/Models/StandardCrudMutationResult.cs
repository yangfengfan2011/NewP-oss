namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudMutationResult
{
    public required bool Succeeded { get; init; }
    public StandardCrudDetail? Detail { get; init; }
    public StandardCrudError? Error { get; init; }

    /// <summary>
    /// 返回标准 CRUD 变更成功结果，允许携带最新详情供 Controller 直接映射。
    /// </summary>
    public static StandardCrudMutationResult Success(StandardCrudDetail? detail = null) =>
        new() { Succeeded = true, Detail = detail };

    /// <summary>
    /// 返回标准 CRUD 变更失败结果，错误对象承载稳定状态码和原因键。
    /// </summary>
    public static StandardCrudMutationResult Failure(StandardCrudError error) =>
        new() { Succeeded = false, Error = error };
}
