using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ICP.Platform.Persistence.EfCore.Migrations;

/// <summary>
/// 表示 owner 自有必填字段转换中的一次性有类型标量回填；执行器只更新目标列为 NULL 的行。
/// </summary>
public sealed class RequiredFieldBackfillOperation : MigrationOperation
{
    public required string OwnerModuleId { get; init; }

    public required string Schema { get; init; }

    public required string Table { get; init; }

    public required string Column { get; init; }

    public required object Value { get; init; }
}
