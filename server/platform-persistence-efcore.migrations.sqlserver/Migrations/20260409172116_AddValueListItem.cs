using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddValueListItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "value_list_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    code = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_value_list_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ValueListItem_Code",
                table: "value_list_items",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "value_list_items");
        }
    }
}
