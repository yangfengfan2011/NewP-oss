using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Workspace.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Workspace_000003_AccountSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 主体固定为账号；只删除冗余类型列，保留空间、根和账号身份。重复账号由新唯一索引拒绝并回滚。
            migrationBuilder.DropIndex(
                name: "UX_WorkspaceSpace_Subject",
                schema: "dbo",
                table: "WorkspaceSpace")
                .Annotation(ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeIndex("Workspace",
                        new ProviderIndexTarget("UX_WorkspaceSpace_Subject",
                            ["SpaceKind", "SubjectModelKey", "SubjectId"], IsUnique: true,
                            IsDescending: null, Filter: null, ProviderAnnotations: [])));

            migrationBuilder.DropColumn(
                name: "SubjectModelKey",
                schema: "dbo",
                table: "WorkspaceSpace")
                .Annotation(ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeColumn("Workspace", 7,
                        new ProviderColumnTarget("SubjectModelKey", "nvarchar(256)", typeof(string).FullName!,
                            IsNullable: true, MaxLength: 256, Precision: null, Scale: null,
                            IsUnicode: null, IsFixedLength: null, Collation: null, ComputedColumnSql: null,
                            IsStored: null, IsIdentity: false, IsRowVersion: false, DefaultValue: null,
                            DefaultValueSql: null, ProviderAnnotations: [])));

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceSpace_Subject",
                schema: "dbo",
                table: "WorkspaceSpace",
                columns: new[] { "SpaceKind", "SubjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 被删除的历史模型键不能从当前账号归属准确还原；恢复旧版本必须使用迁移前备份。
            throw new NotSupportedException("Workspace_000003_AccountSubject requires a verified pre-migration backup for rollback.");
        }
    }
}
