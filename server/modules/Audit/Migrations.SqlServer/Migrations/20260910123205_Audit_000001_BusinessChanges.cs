using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Audit.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Audit_000001_BusinessChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "BusinessChange",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TraceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessChange", x => x.EID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessChange_Actor",
                schema: "dbo",
                table: "BusinessChange",
                columns: new[] { "ActorId", "OccurredAt", "EID" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessChange_Event",
                schema: "dbo",
                table: "BusinessChange",
                columns: new[] { "EventType", "OccurredAt", "EID" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessChange_Target",
                schema: "dbo",
                table: "BusinessChange",
                columns: new[] { "Resource", "TargetId", "OccurredAt", "EID" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessChange_Time",
                schema: "dbo",
                table: "BusinessChange",
                columns: new[] { "OccurredAt", "EID" });

            migrationBuilder.CreateIndex(
                name: "UX_BusinessChange_EID",
                schema: "dbo",
                table: "BusinessChange",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessChange",
                schema: "dbo");
        }
    }
}
