using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000002_GlobalPositionDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Position_Company",
                schema: "dbo",
                table: "Position");

            migrationBuilder.DropIndex(
                name: "IX_Position_Department",
                schema: "dbo",
                table: "Position");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "dbo",
                table: "Position");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                schema: "dbo",
                table: "Position");

            migrationBuilder.DropColumn(
                name: "ECODE",
                schema: "dbo",
                table: "Position");

            migrationBuilder.DropColumn(
                name: "EmploymentStatus",
                schema: "dbo",
                table: "Position");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                schema: "dbo",
                table: "Position");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "dbo",
                table: "Position",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Position_ENAME",
                schema: "dbo",
                table: "Position",
                column: "ENAME",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Position_ENAME",
                schema: "dbo",
                table: "Position");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "dbo",
                table: "Position");

            migrationBuilder.AddColumn<string>(
                name: "CompanyId",
                schema: "dbo",
                table: "Position",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DepartmentId",
                schema: "dbo",
                table: "Position",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ECODE",
                schema: "dbo",
                table: "Position",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmploymentStatus",
                schema: "dbo",
                table: "Position",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SortOrder",
                schema: "dbo",
                table: "Position",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Position_Company",
                schema: "dbo",
                table: "Position",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Position_Department",
                schema: "dbo",
                table: "Position",
                column: "DepartmentId");
        }
    }
}
