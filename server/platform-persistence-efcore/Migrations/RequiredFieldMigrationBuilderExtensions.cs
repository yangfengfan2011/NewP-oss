using Microsoft.EntityFrameworkCore.Migrations;

namespace ICP.Platform.Persistence.EfCore.Migrations;

/// <summary>
/// 提供受限必填字段 migration authoring，避免模块提交任意 SQL。
/// </summary>
public static class RequiredFieldMigrationBuilderExtensions
{
    /// <summary>
    /// 声明只回填 owner 自有目标列中 NULL 行的有类型标量值。
    /// </summary>
    public static void BackfillRequiredField(
        this MigrationBuilder migrationBuilder,
        string ownerModuleId,
        string table,
        string column,
        object value,
        string schema = "dbo")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentNullException.ThrowIfNull(value);

        migrationBuilder.Operations.Add(new RequiredFieldBackfillOperation
        {
            OwnerModuleId = ownerModuleId,
            Schema = schema,
            Table = table,
            Column = column,
            Value = value
        });
    }
}
