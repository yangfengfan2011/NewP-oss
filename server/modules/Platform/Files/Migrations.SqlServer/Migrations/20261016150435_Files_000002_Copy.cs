using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Files.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Files_000002_Copy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileCopyLease",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConfirmedUsageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequestKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceFileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceOwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceOwnerKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SourceUsageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    State = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UsageOwnerKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileCopyLease", x => x.EID);
                    table.ForeignKey(
                        name: "FK_FileCopyLease_FileRecord",
                        column: x => x.FileId,
                        principalSchema: "dbo",
                        principalTable: "FileRecord",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileCopyLease_SourceFileRecord",
                        column: x => x.SourceFileId,
                        principalSchema: "dbo",
                        principalTable: "FileRecord",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileCopyLease_SourceUsage",
                        column: x => x.SourceUsageId,
                        principalSchema: "dbo",
                        principalTable: "FileUsage",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileCopyLease_Expiry",
                schema: "dbo",
                table: "FileCopyLease",
                columns: new[] { "State", "ExpiresAt", "EID" });

            migrationBuilder.CreateIndex(
                name: "IX_FileCopyLease_FileId",
                schema: "dbo",
                table: "FileCopyLease",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FileCopyLease_Request",
                schema: "dbo",
                table: "FileCopyLease",
                columns: new[] { "ActorId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileCopyLease_Source",
                schema: "dbo",
                table: "FileCopyLease",
                columns: new[] { "SourceFileId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_FileCopyLease_SourceUsageId",
                schema: "dbo",
                table: "FileCopyLease",
                column: "SourceUsageId");

            migrationBuilder.CreateIndex(
                name: "UX_FileCopyLease_EID",
                schema: "dbo",
                table: "FileCopyLease",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileCopyLease",
                schema: "dbo");
        }
    }
}
