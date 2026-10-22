using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000004_PersonnelTemporalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                schema: "dbo",
                table: "Person",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmploymentEndedAt",
                schema: "dbo",
                table: "Person",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmploymentStartedAt",
                schema: "dbo",
                table: "Person",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BirthDate",
                schema: "dbo",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "EmploymentEndedAt",
                schema: "dbo",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "EmploymentStartedAt",
                schema: "dbo",
                table: "Person");
        }
    }
}
