using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Example.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Example_000001_Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "ExampleRecord",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ECODE = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExampleRecord", x => x.EID);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ExampleRecord_ECODE",
                schema: "dbo",
                table: "ExampleRecord",
                column: "ECODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ExampleRecord_EID",
                schema: "dbo",
                table: "ExampleRecord",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExampleRecord",
                schema: "dbo");
        }
    }
}
