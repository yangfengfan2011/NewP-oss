using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000006_CompanyStandardCrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Person_CompanyId",
                schema: "dbo",
                table: "Person",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_Company_ENAME",
                schema: "dbo",
                table: "Company",
                column: "ENAME",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Department_Company",
                schema: "dbo",
                table: "Department",
                column: "CompanyId",
                principalSchema: "dbo",
                principalTable: "Company",
                principalColumn: "EID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Person_Company",
                schema: "dbo",
                table: "Person",
                column: "CompanyId",
                principalSchema: "dbo",
                principalTable: "Company",
                principalColumn: "EID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Department_Company",
                schema: "dbo",
                table: "Department");

            migrationBuilder.DropForeignKey(
                name: "FK_Person_Company",
                schema: "dbo",
                table: "Person");

            migrationBuilder.DropIndex(
                name: "IX_Person_CompanyId",
                schema: "dbo",
                table: "Person");

            migrationBuilder.DropIndex(
                name: "UX_Company_ENAME",
                schema: "dbo",
                table: "Company");
        }
    }
}
