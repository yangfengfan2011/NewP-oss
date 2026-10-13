using Microsoft.EntityFrameworkCore.Migrations;

using ICP.Platform.Persistence.ProviderTargets;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000007_RootDepartmentStandardCrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Person_CompanyId",
                schema: "dbo",
                table: "Person")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeIndex(
                        "Organization",
                        new ProviderIndexTarget(
                            "IX_Person_CompanyId",
                            ["CompanyId"],
                            IsUnique: false,
                            IsDescending: null,
                            Filter: null,
                            ProviderAnnotations: [])));

            migrationBuilder.DropIndex(
                name: "IX_Department_Company_Parent_ECODE",
                schema: "dbo",
                table: "Department")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeIndex(
                        "Organization",
                        new ProviderIndexTarget(
                            "IX_Department_Company_Parent_ECODE",
                            ["CompanyId", "ParentDepartmentId", "ECODE"],
                            IsUnique: false,
                            IsDescending: null,
                            Filter: null,
                            ProviderAnnotations: [])));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Department_CompanyId_EID",
                schema: "dbo",
                table: "Department",
                columns: new[] { "CompanyId", "EID" });

            migrationBuilder.CreateIndex(
                name: "IX_Person_CompanyId_DepartmentId",
                schema: "dbo",
                table: "Person",
                columns: new[] { "CompanyId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "UX_Department_Company_ECODE",
                schema: "dbo",
                table: "Department",
                columns: new[] { "CompanyId", "ECODE" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Department_Company_ENAME",
                schema: "dbo",
                table: "Department",
                columns: new[] { "CompanyId", "ENAME" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Person_Company_Department",
                schema: "dbo",
                table: "Person",
                columns: new[] { "CompanyId", "DepartmentId" },
                principalSchema: "dbo",
                principalTable: "Department",
                principalColumns: new[] { "CompanyId", "EID" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Person_Company_Department",
                schema: "dbo",
                table: "Person");

            migrationBuilder.DropIndex(
                name: "IX_Person_CompanyId_DepartmentId",
                schema: "dbo",
                table: "Person");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Department_CompanyId_EID",
                schema: "dbo",
                table: "Department");

            migrationBuilder.DropIndex(
                name: "UX_Department_Company_ECODE",
                schema: "dbo",
                table: "Department");

            migrationBuilder.DropIndex(
                name: "UX_Department_Company_ENAME",
                schema: "dbo",
                table: "Department");

            migrationBuilder.CreateIndex(
                name: "IX_Person_CompanyId",
                schema: "dbo",
                table: "Person",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Department_Company_Parent_ECODE",
                schema: "dbo",
                table: "Department",
                columns: new[] { "CompanyId", "ParentDepartmentId", "ECODE" });
        }
    }
}
