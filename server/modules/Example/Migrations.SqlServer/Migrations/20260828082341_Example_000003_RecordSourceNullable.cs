using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Example.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Example_000003_RecordSourceNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordSource",
                schema: "dbo",
                table: "ExampleRecord",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordSource",
                schema: "dbo",
                table: "ExampleRecord");
        }
    }
}
