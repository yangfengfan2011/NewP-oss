using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Workspace.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Workspace_000006_ScopeNeutral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsScopeNeutral",
                schema: "dbo",
                table: "WorkspaceFolderContent",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsScopeNeutral",
                schema: "dbo",
                table: "WorkspaceFolderContent");
        }
    }
}
