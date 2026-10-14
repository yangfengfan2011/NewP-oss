using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Document.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Document_000003_FileLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentFileLink",
                schema: "dbo",
                columns: table => new
                {
                    LID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FileUsageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LCreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LUpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Ordinal = table.Column<long>(type: "bigint", nullable: false),
                    PEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    __ordinal_Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, computedColumnSql: "isnull(CONVERT([nvarchar](64),hashbytes('SHA2_256',CONVERT([varbinary](max),[Role])),(2)),N'')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFileLink", x => x.LID);
                    table.ForeignKey(
                        name: "FK_DocumentFileLink_DocumentIteration",
                        column: x => x.PEID,
                        principalSchema: "dbo",
                        principalTable: "DocumentIteration",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFileLink_CEID",
                schema: "dbo",
                table: "DocumentFileLink",
                column: "CEID");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFileLink_PEID",
                schema: "dbo",
                table: "DocumentFileLink",
                column: "PEID");

            migrationBuilder.CreateIndex(
                name: "UX_DocumentFileLink_File",
                schema: "dbo",
                table: "DocumentFileLink",
                columns: new[] { "PEID", "CEID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DocumentFileLink_LID",
                schema: "dbo",
                table: "DocumentFileLink",
                column: "LID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DocumentFileLink_Ordinal",
                schema: "dbo",
                table: "DocumentFileLink",
                columns: new[] { "PEID", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentFileLink",
                schema: "dbo");
        }
    }
}
