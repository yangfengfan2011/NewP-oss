using System;
using ICP.Platform.Persistence.EfCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Document.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Document_000002_InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LatestVersionId",
                schema: "dbo",
                table: "Document",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true).RequireEmptyTable();

            migrationBuilder.CreateTable(
                name: "DocumentVersion",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CurrentIterationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DocumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    VersionLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    VersionOrdinal = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersion", x => x.EID);
                    table.ForeignKey(
                        name: "FK_DocumentVersion_Document",
                        column: x => x.DocumentId,
                        principalSchema: "dbo",
                        principalTable: "Document",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentIteration",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DatasetTypeKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ECODE = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IterationOrdinal = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    VersionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentIteration", x => x.EID);
                    table.ForeignKey(
                        name: "FK_DocumentIteration_DocumentVersion",
                        column: x => x.VersionId,
                        principalSchema: "dbo",
                        principalTable: "DocumentVersion",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_DocumentIteration_EID",
                schema: "dbo",
                table: "DocumentIteration",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DocumentIteration_Ordinal",
                schema: "dbo",
                table: "DocumentIteration",
                columns: new[] { "VersionId", "IterationOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DocumentVersion_EID",
                schema: "dbo",
                table: "DocumentVersion",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DocumentVersion_Ordinal",
                schema: "dbo",
                table: "DocumentVersion",
                columns: new[] { "DocumentId", "VersionOrdinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentIteration",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DocumentVersion",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "LatestVersionId",
                schema: "dbo",
                table: "Document");
        }
    }
}
