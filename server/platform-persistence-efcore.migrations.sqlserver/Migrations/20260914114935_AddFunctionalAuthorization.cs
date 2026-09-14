using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFunctionalAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FunctionPoint",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PositionKey = table.Column<string>(type: "nvarchar(max)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    __ordinal_PositionKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, computedColumnSql: "isnull(CONVERT([nvarchar](64),hashbytes('SHA2_256',CONVERT([varbinary](max),[PositionKey])),(2)),N'')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionPoint", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "RoleFunctionGrant",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FunctionPointId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrantedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RoleId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleFunctionGrant", x => x.EID);
                });

            migrationBuilder.CreateIndex(
                name: "UX_FunctionPoint_EID",
                table: "FunctionPoint",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FunctionPoint_PositionKey",
                table: "FunctionPoint",
                column: "__ordinal_PositionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleFunctionGrant_Function_Role",
                table: "RoleFunctionGrant",
                columns: new[] { "FunctionPointId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "UX_RoleFunctionGrant_EID",
                table: "RoleFunctionGrant",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RoleFunctionGrant_Role_Function",
                table: "RoleFunctionGrant",
                columns: new[] { "RoleId", "FunctionPointId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FunctionPoint");

            migrationBuilder.DropTable(
                name: "RoleFunctionGrant");
        }
    }
}
