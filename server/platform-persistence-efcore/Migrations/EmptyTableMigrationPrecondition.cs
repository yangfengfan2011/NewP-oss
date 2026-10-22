using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace ICP.Platform.Persistence.EfCore.Migrations;

/// <summary>在结构扩展前显式要求 owner 自有旧表为空；不删除数据或开放 SQL。</summary>
public static class EmptyTableMigrationPrecondition
{
    public const string AnnotationName = "NewP:RequireEmptyTable/v1";

    /// <summary>给新增列登记封闭空表前提；Host 在本 step 的任何 DDL 前检查。</summary>
    public static OperationBuilder<AddColumnOperation> RequireEmptyTable(this OperationBuilder<AddColumnOperation> operation)
        => operation.Annotation(AnnotationName, true);
}
