using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AlignOrganizationSchemaDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [Person] SET [ENAME] = [Name] WHERE [ENAME] = '' AND [Name] <> ''");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Person");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ValueListItem",
                newName: "ENAME");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "ValueListItem",
                newName: "ECODE");

            migrationBuilder.RenameIndex(
                name: "UX_ValueListItem_Code",
                table: "ValueListItem",
                newName: "UX_ValueListItem_ECODE");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Role",
                newName: "ENAME");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Role",
                newName: "ECODE");

            migrationBuilder.RenameIndex(
                name: "UX_Role_Code",
                table: "Role",
                newName: "UX_Role_ECODE");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Position",
                newName: "ENAME");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Position",
                newName: "ECODE");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "Permission",
                newName: "ENAME");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Permission",
                newName: "ECODE");

            migrationBuilder.RenameIndex(
                name: "UX_Permission_Code",
                table: "Permission",
                newName: "UX_Permission_ECODE");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Department",
                newName: "ENAME");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Department",
                newName: "ECODE");

            migrationBuilder.RenameIndex(
                name: "IX_Department_Company_Parent_Code",
                table: "Department",
                newName: "IX_Department_Company_Parent_ECODE");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Company",
                newName: "ENAME");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "Company",
                newName: "ECODE");

            migrationBuilder.RenameIndex(
                name: "UX_Company_Code",
                table: "Company",
                newName: "UX_Company_ECODE");

            migrationBuilder.AlterColumn<string>(
                name: "PersonId",
                table: "PersonUserLink",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ENAME",
                table: "ValueListItem",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "ECODE",
                table: "ValueListItem",
                newName: "Code");

            migrationBuilder.RenameIndex(
                name: "UX_ValueListItem_ECODE",
                table: "ValueListItem",
                newName: "UX_ValueListItem_Code");

            migrationBuilder.RenameColumn(
                name: "ENAME",
                table: "Role",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "ECODE",
                table: "Role",
                newName: "Code");

            migrationBuilder.RenameIndex(
                name: "UX_Role_ECODE",
                table: "Role",
                newName: "UX_Role_Code");

            migrationBuilder.RenameColumn(
                name: "ENAME",
                table: "Position",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "ECODE",
                table: "Position",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "ENAME",
                table: "Permission",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "ECODE",
                table: "Permission",
                newName: "Code");

            migrationBuilder.RenameIndex(
                name: "UX_Permission_ECODE",
                table: "Permission",
                newName: "UX_Permission_Code");

            migrationBuilder.RenameColumn(
                name: "ENAME",
                table: "Department",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "ECODE",
                table: "Department",
                newName: "Code");

            migrationBuilder.RenameIndex(
                name: "IX_Department_Company_Parent_ECODE",
                table: "Department",
                newName: "IX_Department_Company_Parent_Code");

            migrationBuilder.RenameColumn(
                name: "ENAME",
                table: "Company",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "ECODE",
                table: "Company",
                newName: "Code");

            migrationBuilder.RenameIndex(
                name: "UX_Company_ECODE",
                table: "Company",
                newName: "UX_Company_Code");

            migrationBuilder.AlterColumn<string>(
                name: "PersonId",
                table: "PersonUserLink",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Person",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE [Person] SET [Name] = [ENAME]");
        }
    }
}
