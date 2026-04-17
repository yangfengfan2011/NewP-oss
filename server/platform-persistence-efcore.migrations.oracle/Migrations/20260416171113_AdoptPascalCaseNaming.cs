using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.OracleMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AdoptPascalCaseNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_value_list_items",
                table: "value_list_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_org_positions",
                table: "org_positions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_org_persons",
                table: "org_persons");

            migrationBuilder.DropPrimaryKey(
                name: "PK_org_person_user_links",
                table: "org_person_user_links");

            migrationBuilder.DropPrimaryKey(
                name: "PK_org_departments",
                table: "org_departments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_org_companies",
                table: "org_companies");

            migrationBuilder.DropPrimaryKey(
                name: "PK_auth_user_roles",
                table: "auth_user_roles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_auth_roles",
                table: "auth_roles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_auth_role_permissions",
                table: "auth_role_permissions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_auth_permissions",
                table: "auth_permissions");

            migrationBuilder.RenameTable(
                name: "value_list_items",
                newName: "ValueListItem");

            migrationBuilder.RenameTable(
                name: "org_positions",
                newName: "Position");

            migrationBuilder.RenameTable(
                name: "org_persons",
                newName: "Person");

            migrationBuilder.RenameTable(
                name: "org_person_user_links",
                newName: "PersonUserLink");

            migrationBuilder.RenameTable(
                name: "org_departments",
                newName: "Department");

            migrationBuilder.RenameTable(
                name: "org_companies",
                newName: "Company");

            migrationBuilder.RenameTable(
                name: "auth_user_roles",
                newName: "UserRole");

            migrationBuilder.RenameTable(
                name: "auth_roles",
                newName: "Role");

            migrationBuilder.RenameTable(
                name: "auth_role_permissions",
                newName: "RolePermission");

            migrationBuilder.RenameTable(
                name: "auth_permissions",
                newName: "Permission");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ValueListItem",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "ValueListItem",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "ValueListItem",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ValueListItem",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "ValueListItem",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "ValueListItem",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ValueListItem",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Position",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Position",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Position",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Position",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "sort_order",
                table: "Position",
                newName: "SortOrder");

            migrationBuilder.RenameColumn(
                name: "is_enabled",
                table: "Position",
                newName: "IsEnabled");

            migrationBuilder.RenameColumn(
                name: "employment_status",
                table: "Position",
                newName: "EmploymentStatus");

            migrationBuilder.RenameColumn(
                name: "department_id",
                table: "Position",
                newName: "DepartmentId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Position",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "Position",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Person",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Person",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Person",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "position_id",
                table: "Person",
                newName: "PositionId");

            migrationBuilder.RenameColumn(
                name: "person_no",
                table: "Person",
                newName: "PersonNo");

            migrationBuilder.RenameColumn(
                name: "is_enabled",
                table: "Person",
                newName: "IsEnabled");

            migrationBuilder.RenameColumn(
                name: "employment_status",
                table: "Person",
                newName: "EmploymentStatus");

            migrationBuilder.RenameColumn(
                name: "department_id",
                table: "Person",
                newName: "DepartmentId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Person",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "Person",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "PersonUserLink",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "linked_by",
                table: "PersonUserLink",
                newName: "LinkedBy");

            migrationBuilder.RenameColumn(
                name: "linked_at",
                table: "PersonUserLink",
                newName: "LinkedAt");

            migrationBuilder.RenameColumn(
                name: "person_id",
                table: "PersonUserLink",
                newName: "PersonId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Department",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Department",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Department",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Department",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "sort_order",
                table: "Department",
                newName: "SortOrder");

            migrationBuilder.RenameColumn(
                name: "parent_department_id",
                table: "Department",
                newName: "ParentDepartmentId");

            migrationBuilder.RenameColumn(
                name: "is_enabled",
                table: "Department",
                newName: "IsEnabled");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Department",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "Department",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Company",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Company",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Company",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Company",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "sort_order",
                table: "Company",
                newName: "SortOrder");

            migrationBuilder.RenameColumn(
                name: "is_enabled",
                table: "Company",
                newName: "IsEnabled");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Company",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "UserRole",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "UserRole",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "role_id",
                table: "UserRole",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "assigned_by",
                table: "UserRole",
                newName: "AssignedBy");

            migrationBuilder.RenameColumn(
                name: "assigned_at",
                table: "UserRole",
                newName: "AssignedAt");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Role",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "Role",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Role",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Role",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "Role",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Role",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "is_enabled",
                table: "Role",
                newName: "IsEnabled");

            migrationBuilder.RenameColumn(
                name: "is_built_in",
                table: "Role",
                newName: "IsBuiltIn");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "Role",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Role",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "RolePermission",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "role_id",
                table: "RolePermission",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "permission_id",
                table: "RolePermission",
                newName: "PermissionId");

            migrationBuilder.RenameColumn(
                name: "granted_by",
                table: "RolePermission",
                newName: "GrantedBy");

            migrationBuilder.RenameColumn(
                name: "granted_at",
                table: "RolePermission",
                newName: "GrantedAt");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "Permission",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Permission",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Permission",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "permission_group",
                table: "Permission",
                newName: "Group");

            migrationBuilder.RenameColumn(
                name: "module_id",
                table: "Permission",
                newName: "ModuleId");

            migrationBuilder.RenameColumn(
                name: "display_name",
                table: "Permission",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Permission",
                newName: "CreatedAt");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ValueListItem",
                table: "ValueListItem",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Position",
                table: "Position",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Person",
                table: "Person",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PersonUserLink",
                table: "PersonUserLink",
                column: "PersonId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Department",
                table: "Department",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Company",
                table: "Company",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserRole",
                table: "UserRole",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Role",
                table: "Role",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RolePermission",
                table: "RolePermission",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Permission",
                table: "Permission",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ValueListItem",
                table: "ValueListItem");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserRole",
                table: "UserRole");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RolePermission",
                table: "RolePermission");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Role",
                table: "Role");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Position",
                table: "Position");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PersonUserLink",
                table: "PersonUserLink");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Person",
                table: "Person");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Permission",
                table: "Permission");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Department",
                table: "Department");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Company",
                table: "Company");

            migrationBuilder.RenameTable(
                name: "ValueListItem",
                newName: "value_list_items");

            migrationBuilder.RenameTable(
                name: "UserRole",
                newName: "auth_user_roles");

            migrationBuilder.RenameTable(
                name: "RolePermission",
                newName: "auth_role_permissions");

            migrationBuilder.RenameTable(
                name: "Role",
                newName: "auth_roles");

            migrationBuilder.RenameTable(
                name: "Position",
                newName: "org_positions");

            migrationBuilder.RenameTable(
                name: "PersonUserLink",
                newName: "org_person_user_links");

            migrationBuilder.RenameTable(
                name: "Person",
                newName: "org_persons");

            migrationBuilder.RenameTable(
                name: "Permission",
                newName: "auth_permissions");

            migrationBuilder.RenameTable(
                name: "Department",
                newName: "org_departments");

            migrationBuilder.RenameTable(
                name: "Company",
                newName: "org_companies");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "value_list_items",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "value_list_items",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "value_list_items",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "value_list_items",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "value_list_items",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "value_list_items",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "value_list_items",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "auth_user_roles",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "auth_user_roles",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                table: "auth_user_roles",
                newName: "role_id");

            migrationBuilder.RenameColumn(
                name: "AssignedBy",
                table: "auth_user_roles",
                newName: "assigned_by");

            migrationBuilder.RenameColumn(
                name: "AssignedAt",
                table: "auth_user_roles",
                newName: "assigned_at");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "auth_role_permissions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                table: "auth_role_permissions",
                newName: "role_id");

            migrationBuilder.RenameColumn(
                name: "PermissionId",
                table: "auth_role_permissions",
                newName: "permission_id");

            migrationBuilder.RenameColumn(
                name: "GrantedBy",
                table: "auth_role_permissions",
                newName: "granted_by");

            migrationBuilder.RenameColumn(
                name: "GrantedAt",
                table: "auth_role_permissions",
                newName: "granted_at");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "auth_roles",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "auth_roles",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "auth_roles",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "auth_roles",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "auth_roles",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "auth_roles",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "auth_roles",
                newName: "is_enabled");

            migrationBuilder.RenameColumn(
                name: "IsBuiltIn",
                table: "auth_roles",
                newName: "is_built_in");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "auth_roles",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "auth_roles",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "org_positions",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "org_positions",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "org_positions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "org_positions",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SortOrder",
                table: "org_positions",
                newName: "sort_order");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "org_positions",
                newName: "is_enabled");

            migrationBuilder.RenameColumn(
                name: "EmploymentStatus",
                table: "org_positions",
                newName: "employment_status");

            migrationBuilder.RenameColumn(
                name: "DepartmentId",
                table: "org_positions",
                newName: "department_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "org_positions",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "org_positions",
                newName: "company_id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "org_person_user_links",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "LinkedBy",
                table: "org_person_user_links",
                newName: "linked_by");

            migrationBuilder.RenameColumn(
                name: "LinkedAt",
                table: "org_person_user_links",
                newName: "linked_at");

            migrationBuilder.RenameColumn(
                name: "PersonId",
                table: "org_person_user_links",
                newName: "person_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "org_persons",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "org_persons",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "org_persons",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "PositionId",
                table: "org_persons",
                newName: "position_id");

            migrationBuilder.RenameColumn(
                name: "PersonNo",
                table: "org_persons",
                newName: "person_no");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "org_persons",
                newName: "is_enabled");

            migrationBuilder.RenameColumn(
                name: "EmploymentStatus",
                table: "org_persons",
                newName: "employment_status");

            migrationBuilder.RenameColumn(
                name: "DepartmentId",
                table: "org_persons",
                newName: "department_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "org_persons",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "org_persons",
                newName: "company_id");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "auth_permissions",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "auth_permissions",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "auth_permissions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ModuleId",
                table: "auth_permissions",
                newName: "module_id");

            migrationBuilder.RenameColumn(
                name: "Group",
                table: "auth_permissions",
                newName: "permission_group");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "auth_permissions",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "auth_permissions",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "org_departments",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "org_departments",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "org_departments",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "org_departments",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SortOrder",
                table: "org_departments",
                newName: "sort_order");

            migrationBuilder.RenameColumn(
                name: "ParentDepartmentId",
                table: "org_departments",
                newName: "parent_department_id");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "org_departments",
                newName: "is_enabled");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "org_departments",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "org_departments",
                newName: "company_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "org_companies",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "org_companies",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "org_companies",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "org_companies",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SortOrder",
                table: "org_companies",
                newName: "sort_order");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "org_companies",
                newName: "is_enabled");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "org_companies",
                newName: "created_at");

            migrationBuilder.AddPrimaryKey(
                name: "PK_value_list_items",
                table: "value_list_items",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auth_user_roles",
                table: "auth_user_roles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auth_role_permissions",
                table: "auth_role_permissions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auth_roles",
                table: "auth_roles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_org_positions",
                table: "org_positions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_org_person_user_links",
                table: "org_person_user_links",
                column: "person_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_org_persons",
                table: "org_persons",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auth_permissions",
                table: "auth_permissions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_org_departments",
                table: "org_departments",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_org_companies",
                table: "org_companies",
                column: "id");
        }
    }
}
