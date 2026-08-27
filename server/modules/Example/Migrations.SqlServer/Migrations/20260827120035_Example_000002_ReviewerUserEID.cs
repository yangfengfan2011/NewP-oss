using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Example.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Example_000002_ReviewerUserEID : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewerUserEID",
                schema: "dbo",
                table: "ExampleRecord",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewerUserEID",
                schema: "dbo",
                table: "ExampleRecord");
        }
    }
}
