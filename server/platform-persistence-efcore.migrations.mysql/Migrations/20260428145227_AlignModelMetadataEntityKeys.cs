using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AlignModelMetadataEntityKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PersonUserLink",
                table: "PersonUserLink");

            migrationBuilder.DropIndex(
                name: "IX_Person_Company",
                table: "Person");

            migrationBuilder.DropIndex(
                name: "IX_Person_Department",
                table: "Person");

            migrationBuilder.DropIndex(
                name: "UX_Person_Company_PersonNo",
                table: "Person");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "ValueListItem",
                newName: "EID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "UserRole",
                newName: "EID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "RolePermission",
                newName: "EID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Role",
                newName: "EID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Position",
                newName: "EID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Person",
                newName: "EID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Permission",
                newName: "EID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Department",
                newName: "EID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Company",
                newName: "EID");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "ValueListItem",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "UserRole",
                type: "varchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "UserRole",
                type: "varchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedBy",
                table: "UserRole",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "UserRole",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "UserRole",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "UserRole",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "UserRole",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "RolePermission",
                type: "varchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "PermissionId",
                table: "RolePermission",
                type: "varchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "GrantedBy",
                table: "RolePermission",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "RolePermission",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "RolePermission",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "RolePermission",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "RolePermission",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Position",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Position",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EID",
                table: "PersonUserLink",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PersonUserLink",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "PersonUserLink",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "PersonUserLink",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "PersonUserLink",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Person",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ENAME",
                table: "Person",
                type: "varchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Person",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Permission",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Permission",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Permission",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Department",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Department",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Company",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Company",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql("UPDATE `PersonUserLink` SET `EID` = `PersonId` WHERE `EID` = ''");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PersonUserLink",
                table: "PersonUserLink",
                column: "EID");

            migrationBuilder.CreateIndex(
                name: "UX_ValueListItem_EID",
                table: "ValueListItem",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_UserRole_EID",
                table: "UserRole",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RolePermission_EID",
                table: "RolePermission",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Role_EID",
                table: "Role",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Position_EID",
                table: "Position",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PersonUserLink_EID",
                table: "PersonUserLink",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Person_EID",
                table: "Person",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Permission_EID",
                table: "Permission",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Department_EID",
                table: "Department",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Company_EID",
                table: "Company",
                column: "EID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ValueListItem_EID",
                table: "ValueListItem");

            migrationBuilder.DropIndex(
                name: "UX_UserRole_EID",
                table: "UserRole");

            migrationBuilder.DropIndex(
                name: "UX_RolePermission_EID",
                table: "RolePermission");

            migrationBuilder.DropIndex(
                name: "UX_Role_EID",
                table: "Role");

            migrationBuilder.DropIndex(
                name: "UX_Position_EID",
                table: "Position");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PersonUserLink",
                table: "PersonUserLink");

            migrationBuilder.DropIndex(
                name: "UX_PersonUserLink_EID",
                table: "PersonUserLink");

            migrationBuilder.DropIndex(
                name: "UX_Person_EID",
                table: "Person");

            migrationBuilder.DropIndex(
                name: "UX_Permission_EID",
                table: "Permission");

            migrationBuilder.DropIndex(
                name: "UX_Department_EID",
                table: "Department");

            migrationBuilder.DropIndex(
                name: "UX_Company_EID",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "ValueListItem");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "UserRole");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "UserRole");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "UserRole");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "UserRole");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "RolePermission");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "RolePermission");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "RolePermission");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "RolePermission");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Position");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Position");

            migrationBuilder.DropColumn(
                name: "EID",
                table: "PersonUserLink");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PersonUserLink");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "PersonUserLink");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PersonUserLink");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "PersonUserLink");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "ENAME",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Person");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Permission");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Permission");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Permission");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Department");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Department");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Company");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "ValueListItem",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "UserRole",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "RolePermission",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "Role",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "Position",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "Person",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "Permission",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "Department",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "EID",
                table: "Company",
                newName: "Id");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "UserRole",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "UserRole",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedBy",
                table: "UserRole",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "RolePermission",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "PermissionId",
                table: "RolePermission",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "GrantedBy",
                table: "RolePermission",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PersonUserLink",
                table: "PersonUserLink",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Person_Company",
                table: "Person",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Person_Department",
                table: "Person",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "UX_Person_Company_PersonNo",
                table: "Person",
                columns: new[] { "CompanyId", "PersonNo" },
                unique: true);
        }
    }
}
