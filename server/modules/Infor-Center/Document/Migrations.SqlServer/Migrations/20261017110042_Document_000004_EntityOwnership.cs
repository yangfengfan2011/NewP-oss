using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Document.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Document_000004_EntityOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerDepartmentId",
                schema: "dbo",
                table: "Document",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                schema: "dbo",
                table: "Document",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerDepartmentId",
                schema: "dbo",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "dbo",
                table: "Document");
        }
    }
}
