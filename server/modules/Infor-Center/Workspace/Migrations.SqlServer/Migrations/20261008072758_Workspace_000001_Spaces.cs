using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Workspace.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Workspace_000001_Spaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "WorkspaceFolder",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OriginSpaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceFolder", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceSpace",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RootFolderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SpaceKind = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubjectModelKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceSpace", x => x.EID);
                    table.ForeignKey(
                        name: "FK_WorkspaceSpace_Root",
                        column: x => x.RootFolderId,
                        principalSchema: "dbo",
                        principalTable: "WorkspaceFolder",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceFolder_Origin",
                schema: "dbo",
                table: "WorkspaceFolder",
                column: "OriginSpaceId");

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceFolder_EID",
                schema: "dbo",
                table: "WorkspaceFolder",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceSpace_EID",
                schema: "dbo",
                table: "WorkspaceSpace",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceSpace_Root",
                schema: "dbo",
                table: "WorkspaceSpace",
                column: "RootFolderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceSpace_Subject",
                schema: "dbo",
                table: "WorkspaceSpace",
                columns: new[] { "SpaceKind", "SubjectModelKey", "SubjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceSpace",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "WorkspaceFolder",
                schema: "dbo");
        }
    }
}
