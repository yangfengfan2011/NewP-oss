using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Workspace.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Workspace_000009_TransferNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceTransferNotification",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DirectoryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LinkId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ObjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecipientId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SenderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SpaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceTransferNotification", x => x.EID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTransferNotification_Recipient",
                schema: "dbo",
                table: "WorkspaceTransferNotification",
                columns: new[] { "RecipientId", "CreatedAt", "EID" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkspaceTransferNotification_EID",
                schema: "dbo",
                table: "WorkspaceTransferNotification",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceTransferNotification",
                schema: "dbo");
        }
    }
}
