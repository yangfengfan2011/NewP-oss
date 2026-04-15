using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AlignConfigItemMetadataStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_config_items",
                table: "config_items");

            migrationBuilder.DropIndex(
                name: "IX_ConfigItem_Module",
                table: "config_items");

            migrationBuilder.DropIndex(
                name: "UX_ConfigItem_Key_Environment_Tenant",
                table: "config_items");

            migrationBuilder.RenameTable(
                name: "config_items",
                newName: "ConfigItem");

            migrationBuilder.RenameColumn(
                name: "environment",
                table: "ConfigItem",
                newName: "Environment");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "ConfigItem",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "ConfigItem",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "ConfigItem",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "ConfigItem",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "module_id",
                table: "ConfigItem",
                newName: "ModuleId");

            migrationBuilder.RenameColumn(
                name: "is_sensitive",
                table: "ConfigItem",
                newName: "IsSensitive");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ConfigItem",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "config_value",
                table: "ConfigItem",
                newName: "Value");

            migrationBuilder.RenameColumn(
                name: "config_type",
                table: "ConfigItem",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "config_key",
                table: "ConfigItem",
                newName: "Key");

            migrationBuilder.RenameColumn(
                name: "config_group",
                table: "ConfigItem",
                newName: "Group");

            migrationBuilder.RenameColumn(
                name: "display_name",
                table: "ConfigItem",
                newName: "ENAME");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ConfigItem",
                newName: "EID");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "ConfigItem",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ConfigItem",
                table: "ConfigItem",
                column: "EID");

            migrationBuilder.CreateIndex(
                name: "UX_ConfigItem_EID",
                table: "ConfigItem",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ConfigItem",
                table: "ConfigItem");

            migrationBuilder.DropIndex(
                name: "UX_ConfigItem_EID",
                table: "ConfigItem");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "ConfigItem");

            migrationBuilder.RenameTable(
                name: "ConfigItem",
                newName: "config_items");

            migrationBuilder.RenameColumn(
                name: "Environment",
                table: "config_items",
                newName: "environment");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "config_items",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Value",
                table: "config_items",
                newName: "config_value");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "config_items",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "config_items",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "config_items",
                newName: "config_type");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "config_items",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ModuleId",
                table: "config_items",
                newName: "module_id");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "config_items",
                newName: "config_key");

            migrationBuilder.RenameColumn(
                name: "IsSensitive",
                table: "config_items",
                newName: "is_sensitive");

            migrationBuilder.RenameColumn(
                name: "Group",
                table: "config_items",
                newName: "config_group");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "config_items",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ENAME",
                table: "config_items",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "config_items",
                newName: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_config_items",
                table: "config_items",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigItem_Module",
                table: "config_items",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "UX_ConfigItem_Key_Environment_Tenant",
                table: "config_items",
                columns: new[] { "config_key", "environment", "tenant_id" },
                unique: true,
                filter: "[environment] IS NOT NULL AND [tenant_id] IS NOT NULL");
        }
    }
}
