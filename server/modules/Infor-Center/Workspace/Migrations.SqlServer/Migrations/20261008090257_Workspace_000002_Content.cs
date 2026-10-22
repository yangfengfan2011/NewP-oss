using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Workspace.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Workspace_000002_Content : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceFolderContent",
                schema: "dbo",
                columns: table => new
                {
                    LID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChildModelKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    LCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LCreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LUpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    __ordinal_ChildModelKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, computedColumnSql: "isnull(CONVERT([nvarchar](64),hashbytes('SHA2_256',CONVERT([varbinary](max),[ChildModelKey])),(2)),N'')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceFolderContent", x => x.LID);
                    table.ForeignKey(
                        name: "FK_WorkspaceFolderContent_Parent",
                        column: x => x.PEID,
                        principalSchema: "dbo",
                        principalTable: "WorkspaceFolder",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceFolderContent_CEID",
                schema: "dbo",
                table: "WorkspaceFolderContent",
                column: "CEID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceFolderContent_PEID",
                schema: "dbo",
                table: "WorkspaceFolderContent",
                column: "PEID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceFolderContent_Reverse",
                schema: "dbo",
                table: "WorkspaceFolderContent",
                columns: new[] { "__ordinal_ChildModelKey", "CEID", "PEID" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceFolderContent_LID",
                schema: "dbo",
                table: "WorkspaceFolderContent",
                column: "LID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceFolderContent_Target",
                schema: "dbo",
                table: "WorkspaceFolderContent",
                columns: new[] { "PEID", "__ordinal_ChildModelKey", "CEID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceFolderContent",
                schema: "dbo");
        }
    }
}
