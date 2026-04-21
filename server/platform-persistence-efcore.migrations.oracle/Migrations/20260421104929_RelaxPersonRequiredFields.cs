using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.OracleMigrations.Migrations
{
    /// <inheritdoc />
    public partial class RelaxPersonRequiredFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Person_Company_PersonNo",
                table: "Person");

            migrationBuilder.AlterColumn<string>(
                name: "EmploymentStatus",
                table: "Person",
                type: "NVARCHAR2(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "NVARCHAR2(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "DepartmentId",
                table: "Person",
                type: "NVARCHAR2(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "NVARCHAR2(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "CompanyId",
                table: "Person",
                type: "NVARCHAR2(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "NVARCHAR2(128)",
                oldMaxLength: 128);

            migrationBuilder.CreateIndex(
                name: "UX_Person_Company_PersonNo",
                table: "Person",
                columns: new[] { "CompanyId", "PersonNo" },
                unique: true,
                filter: "\"CompanyId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Person_Company_PersonNo",
                table: "Person");

            migrationBuilder.AlterColumn<string>(
                name: "EmploymentStatus",
                table: "Person",
                type: "NVARCHAR2(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "NVARCHAR2(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DepartmentId",
                table: "Person",
                type: "NVARCHAR2(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "NVARCHAR2(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CompanyId",
                table: "Person",
                type: "NVARCHAR2(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "NVARCHAR2(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Person_Company_PersonNo",
                table: "Person",
                columns: new[] { "CompanyId", "PersonNo" },
                unique: true);
        }
    }
}
