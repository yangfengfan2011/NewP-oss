using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "auth_permissions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    description = table.Column<string>(type: "longtext", nullable: true),
                    display_name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    permission_group = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    module_id = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_permissions", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "auth_role_permissions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    granted_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    granted_by = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    permission_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    role_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_role_permissions", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "auth_roles",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    description = table.Column<string>(type: "longtext", nullable: true),
                    is_built_in = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_by = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_roles", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "auth_user_roles",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    assigned_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    assigned_by = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    role_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    user_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_user_roles", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "config_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    description = table.Column<string>(type: "longtext", nullable: true),
                    display_name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    environment = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    config_group = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    is_sensitive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    config_key = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    module_id = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    tenant_id = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    config_type = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_by = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    config_value = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_items", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Permission_Module",
                table: "auth_permissions",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "UX_Permission_Code",
                table: "auth_permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RolePermission_Role_Permission",
                table: "auth_role_permissions",
                columns: new[] { "role_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Role_Code",
                table: "auth_roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_Role",
                table: "auth_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "UX_UserRole_User_Role",
                table: "auth_user_roles",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfigItem_Module",
                table: "config_items",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "UX_ConfigItem_Key_Environment_Tenant",
                table: "config_items",
                columns: new[] { "config_key", "environment", "tenant_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_permissions");

            migrationBuilder.DropTable(
                name: "auth_role_permissions");

            migrationBuilder.DropTable(
                name: "auth_roles");

            migrationBuilder.DropTable(
                name: "auth_user_roles");

            migrationBuilder.DropTable(
                name: "config_items");
        }
    }
}
