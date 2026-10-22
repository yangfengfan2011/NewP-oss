using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Files.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Files_000001_Lifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "FileRecord",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    State = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StorageProviderKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileRecord", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "FileUploadLease",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConfirmedUsageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UsageOwnerKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileUploadLease", x => x.EID);
                    table.ForeignKey(
                        name: "FK_FileUploadLease_FileRecord",
                        column: x => x.FileId,
                        principalSchema: "dbo",
                        principalTable: "FileRecord",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FileUsage",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OwnerKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OwnerObjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileUsage", x => x.EID);
                    table.ForeignKey(
                        name: "FK_FileUsage_FileRecord",
                        column: x => x.FileId,
                        principalSchema: "dbo",
                        principalTable: "FileRecord",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileRecord_State",
                schema: "dbo",
                table: "FileRecord",
                columns: new[] { "State", "EID" });

            migrationBuilder.CreateIndex(
                name: "UX_FileRecord_EID",
                schema: "dbo",
                table: "FileRecord",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FileRecord_StorageKey",
                schema: "dbo",
                table: "FileRecord",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadLease_Actor",
                schema: "dbo",
                table: "FileUploadLease",
                columns: new[] { "ActorId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadLease_Expiry",
                schema: "dbo",
                table: "FileUploadLease",
                columns: new[] { "State", "ExpiresAt", "EID" });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadLease_FileId",
                schema: "dbo",
                table: "FileUploadLease",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "UX_FileUploadLease_EID",
                schema: "dbo",
                table: "FileUploadLease",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileUsage_Active",
                schema: "dbo",
                table: "FileUsage",
                columns: new[] { "FileId", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_FileUsage_EID",
                schema: "dbo",
                table: "FileUsage",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FileUsage_Owner",
                schema: "dbo",
                table: "FileUsage",
                columns: new[] { "OwnerKey", "OwnerObjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileUploadLease",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FileUsage",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FileRecord",
                schema: "dbo");
        }
    }
}
