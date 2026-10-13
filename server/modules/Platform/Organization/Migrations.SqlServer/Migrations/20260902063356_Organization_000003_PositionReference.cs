using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000003_PositionReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Person_PositionId",
                schema: "dbo",
                table: "Person",
                column: "PositionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Person_Position",
                schema: "dbo",
                table: "Person",
                column: "PositionId",
                principalSchema: "dbo",
                principalTable: "Position",
                principalColumn: "EID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Person_Position",
                schema: "dbo",
                table: "Person");

            migrationBuilder.DropIndex(
                name: "IX_Person_PositionId",
                schema: "dbo",
                table: "Person");
        }
    }
}
