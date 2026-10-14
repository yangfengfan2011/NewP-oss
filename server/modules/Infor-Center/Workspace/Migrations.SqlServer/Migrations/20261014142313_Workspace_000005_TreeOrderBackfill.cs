using Microsoft.EntityFrameworkCore.Migrations;
using ICP.Platform.Persistence.EfCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Workspace.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Workspace_000005_TreeOrderBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 只初始化文件夹关系；名称比较复用升级前数据库规则，Part/Document 保留 NULL。
            migrationBuilder.BackfillRelationOrdinal("Workspace", "WorkspaceFolderContent", "TreeOrdinal", "PEID", "LID", "CEID", "ChildModelKey",
                "WorkspaceFolder", "WorkspaceFolder", "EID", "ENAME");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 顺序已经供运行时消费；回退使用迁移前备份，平台不自动撤销已提交数据迁移。
            throw new NotSupportedException("Restore a verified pre-migration backup to roll back initialized folder order.");
        }
    }
}
