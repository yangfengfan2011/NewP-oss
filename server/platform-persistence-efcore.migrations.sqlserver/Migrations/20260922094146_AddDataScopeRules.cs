using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddDataScopeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataScopeRuleNode",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConditionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ParentId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResourceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RootId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataScopeRuleNode", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "DataScopeRuleSubject",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NodeId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Priority = table.Column<long>(type: "bigint", nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SubjectType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataScopeRuleSubject", x => x.EID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeRuleNode_0",
                table: "DataScopeRuleNode",
                columns: new[] { "RootId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeRuleNode_1",
                table: "DataScopeRuleNode",
                column: "ResourceKey");

            migrationBuilder.CreateIndex(
                name: "UX_DataScopeRuleNode_EID",
                table: "DataScopeRuleNode",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeRuleSubject_0",
                table: "DataScopeRuleSubject",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DataScopeRuleSubject_1",
                table: "DataScopeRuleSubject",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "UX_DataScopeRuleSubject_EID",
                table: "DataScopeRuleSubject",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataScopeRuleNode");

            migrationBuilder.DropTable(
                name: "DataScopeRuleSubject");
        }
    }
}
