namespace ICP.Platform.Crud.Models;

/// <summary>写入已提交但当前请求不能获得对象详情时，仅确认成功事实。</summary>
public sealed record StandardCrudMutationReceipt(bool Succeeded);
