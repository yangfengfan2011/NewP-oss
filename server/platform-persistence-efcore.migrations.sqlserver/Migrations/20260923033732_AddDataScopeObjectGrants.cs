using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDataScopeObjectGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataScopeObjectAction",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActionKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntryId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Opinion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataScopeObjectAction", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "DataScopeObjectSubject",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IncludeDescendants = table.Column<bool>(type: "bit", nullable: false),
                    ObjectId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Priority = table.Column<long>(type: "bigint", nullable: false),
                    ResourceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SubjectType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataScopeObjectSubject", x => x.EID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeObjectAction_0",
                table: "DataScopeObjectAction",
                columns: new[] { "EntryId", "ActionKey" });

            migrationBuilder.CreateIndex(
                name: "UX_DataScopeObjectAction_EID",
                table: "DataScopeObjectAction",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeObjectSubject_0",
                table: "DataScopeObjectSubject",
                columns: new[] { "ResourceKey", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeObjectSubject_1",
                table: "DataScopeObjectSubject",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "UX_DataScopeObjectSubject_EID",
                table: "DataScopeObjectSubject",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataScopeObjectAction");

            migrationBuilder.DropTable(
                name: "DataScopeObjectSubject");
        }
    }
}
