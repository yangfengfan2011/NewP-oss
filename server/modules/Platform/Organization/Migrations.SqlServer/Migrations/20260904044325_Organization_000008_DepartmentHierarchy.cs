using Microsoft.EntityFrameworkCore.Migrations;

using ICP.Platform.Persistence.ProviderTargets;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000008_DepartmentHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Department_Company_ENAME",
                schema: "dbo",
                table: "Department")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeIndex(
                        "Organization",
                        new ProviderIndexTarget(
                            "UX_Department_Company_ENAME",
                            ["CompanyId", "ENAME"],
                            IsUnique: true,
                            IsDescending: null,
                            Filter: null,
                            ProviderAnnotations: [])));

            migrationBuilder.CreateIndex(
                name: "UX_Department_Company_Parent_ENAME",
                schema: "dbo",
                table: "Department",
                columns: new[] { "CompanyId", "ParentDepartmentId", "ENAME" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Department_Company_ParentDepartment",
                schema: "dbo",
                table: "Department",
                columns: new[] { "CompanyId", "ParentDepartmentId" },
                principalSchema: "dbo",
                principalTable: "Department",
                principalColumns: new[] { "CompanyId", "EID" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Department_Company_ParentDepartment",
                schema: "dbo",
                table: "Department");

            migrationBuilder.DropIndex(
                name: "UX_Department_Company_Parent_ENAME",
                schema: "dbo",
                table: "Department");

            migrationBuilder.CreateIndex(
                name: "UX_Department_Company_ENAME",
                schema: "dbo",
                table: "Department",
                columns: new[] { "CompanyId", "ENAME" },
                unique: true);
        }
    }
}
