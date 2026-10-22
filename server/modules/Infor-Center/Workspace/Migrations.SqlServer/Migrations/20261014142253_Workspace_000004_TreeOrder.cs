using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Workspace.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Workspace_000004_TreeOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TreeOrdinal",
                schema: "dbo",
                table: "WorkspaceFolderContent",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceFolderContent_TreeOrdinal",
                schema: "dbo",
                table: "WorkspaceFolderContent",
                columns: new[] { "PEID", "TreeOrdinal" },
                unique: true,
                filter: "[TreeOrdinal] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_WorkspaceFolderContent_TreeOrdinal",
                schema: "dbo",
                table: "WorkspaceFolderContent");

            migrationBuilder.DropColumn(
                name: "TreeOrdinal",
                schema: "dbo",
                table: "WorkspaceFolderContent");
        }
    }
}
