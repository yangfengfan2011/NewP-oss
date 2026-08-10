using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class Organization_PermanentUserBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonUserLink");

            migrationBuilder.AddColumn<string>(
                name: "UserBindingReason",
                table: "Person",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UserBoundAt",
                table: "Person",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserBoundBy",
                table: "Person",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserEID",
                table: "Person",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Person_UserEID",
                table: "Person",
                column: "UserEID",
                unique: true,
                filter: "[UserEID] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 该迁移明确放弃旧绑定行；回滚必须恢复与旧应用匹配的数据库备份，不能重建空旧表。
        }
    }
}
