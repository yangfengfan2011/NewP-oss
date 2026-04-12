using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "org_companies",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    sort_order = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_departments",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    company_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    parent_department_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    sort_order = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_departments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_person_user_links",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    linked_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    linked_by = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    user_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_person_user_links", x => x.person_id);
                });

            migrationBuilder.CreateTable(
                name: "org_persons",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    company_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    department_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    employment_status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    person_no = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    position_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_persons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "org_positions",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    company_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    department_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    employment_status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    sort_order = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_positions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Company_Code",
                table: "org_companies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Department_Company",
                table: "org_departments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_Department_Company_Parent_Code",
                table: "org_departments",
                columns: new[] { "company_id", "parent_department_id", "code" });

            migrationBuilder.CreateIndex(
                name: "IX_Department_Parent",
                table: "org_departments",
                column: "parent_department_id");

            migrationBuilder.CreateIndex(
                name: "UX_PersonUserLink_Person",
                table: "org_person_user_links",
                column: "person_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PersonUserLink_User",
                table: "org_person_user_links",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Person_Company",
                table: "org_persons",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_Person_Department",
                table: "org_persons",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "UX_Person_Company_PersonNo",
                table: "org_persons",
                columns: new[] { "company_id", "person_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Position_Company",
                table: "org_positions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_Position_Department",
                table: "org_positions",
                column: "department_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "org_companies");

            migrationBuilder.DropTable(
                name: "org_departments");

            migrationBuilder.DropTable(
                name: "org_person_user_links");

            migrationBuilder.DropTable(
                name: "org_persons");

            migrationBuilder.DropTable(
                name: "org_positions");
        }
    }
}
