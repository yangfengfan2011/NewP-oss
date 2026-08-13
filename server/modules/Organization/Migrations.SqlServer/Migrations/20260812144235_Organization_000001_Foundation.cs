using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000001_Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "Company",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ECODE = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Company", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "Department",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ECODE = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ParentDepartmentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SortOrder = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Department", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "Person",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AccountBindingReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AccountBoundAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AccountBoundBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AccountEID = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CompanyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DepartmentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EmploymentStatus = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PersonNo = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PositionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Person", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "Position",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CompanyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DepartmentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ECODE = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EmploymentStatus = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Position", x => x.EID);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Company_ECODE",
                schema: "dbo",
                table: "Company",
                column: "ECODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Company_EID",
                schema: "dbo",
                table: "Company",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Department_Company",
                schema: "dbo",
                table: "Department",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Department_Company_Parent_ECODE",
                schema: "dbo",
                table: "Department",
                columns: new[] { "CompanyId", "ParentDepartmentId", "ECODE" });

            migrationBuilder.CreateIndex(
                name: "IX_Department_Parent",
                schema: "dbo",
                table: "Department",
                column: "ParentDepartmentId");

            migrationBuilder.CreateIndex(
                name: "UX_Department_EID",
                schema: "dbo",
                table: "Department",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Person_EID",
                schema: "dbo",
                table: "Person",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Person_AccountEID",
                schema: "dbo",
                table: "Person",
                column: "AccountEID",
                unique: true,
                filter: "[AccountEID] IS NOT NULL");

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

            migrationBuilder.CreateIndex(
                name: "UX_Position_EID",
                schema: "dbo",
                table: "Position",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Company",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Department",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Person",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Position",
                schema: "dbo");
        }
    }
}
