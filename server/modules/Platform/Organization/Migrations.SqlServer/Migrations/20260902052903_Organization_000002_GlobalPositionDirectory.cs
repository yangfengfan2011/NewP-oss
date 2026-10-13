using Microsoft.EntityFrameworkCore.Migrations;

using ICP.Platform.Persistence.ProviderTargets;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000002_GlobalPositionDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Position_Company",
                schema: "dbo",
                table: "Position")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeIndex(
                        "Organization",
                        new ProviderIndexTarget(
                            "IX_Position_Company",
                            ["CompanyId"],
                            IsUnique: false,
                            IsDescending: null,
                            Filter: null,
                            ProviderAnnotations: [])));

            migrationBuilder.DropIndex(
                name: "IX_Position_Department",
                schema: "dbo",
                table: "Position")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeIndex(
                        "Organization",
                        new ProviderIndexTarget(
                            "IX_Position_Department",
                            ["DepartmentId"],
                            IsUnique: false,
                            IsDescending: null,
                            Filter: null,
                            ProviderAnnotations: [])));

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "dbo",
                table: "Position")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeColumn(
                        "Organization",
                        1,
                        RequiredStringColumn("CompanyId", "nvarchar(128)", 128)));

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                schema: "dbo",
                table: "Position")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeColumn(
                        "Organization",
                        3,
                        RequiredStringColumn("DepartmentId", "nvarchar(128)", 128)));

            migrationBuilder.DropColumn(
                name: "ECODE",
                schema: "dbo",
                table: "Position")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeColumn(
                        "Organization",
                        3,
                        RequiredStringColumn("ECODE", "nvarchar(256)", 256)));

            migrationBuilder.DropColumn(
                name: "EmploymentStatus",
                schema: "dbo",
                table: "Position")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeColumn(
                        "Organization",
                        4,
                        RequiredStringColumn("EmploymentStatus", "nvarchar(256)", 256)));

            migrationBuilder.DropColumn(
                name: "SortOrder",
                schema: "dbo",
                table: "Position")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeColumn(
                        "Organization",
                        5,
                        RequiredStringColumn("SortOrder", "nvarchar(256)", 256)));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "dbo",
                table: "Position",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Position_ENAME",
                schema: "dbo",
                table: "Position",
                column: "ENAME",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 该步骤已声明 backup-restore-only；被删除旧列的业务值无法由 migration 安全重建。
            throw new NotSupportedException(
                "Migration 20260902052903_Organization_000002_GlobalPositionDirectory is backup-restore-only; restore a verified database backup instead of running Down.");
        }

        private static ProviderColumnTarget RequiredStringColumn(string name, string storeType, int maxLength) =>
            new(
                name,
                storeType,
                typeof(string).FullName!,
                IsNullable: false,
                MaxLength: maxLength,
                Precision: null,
                Scale: null,
                IsUnicode: null,
                IsFixedLength: null,
                Collation: null,
                ComputedColumnSql: null,
                IsStored: null,
                IsIdentity: false,
                IsRowVersion: false,
                DefaultValue: null,
                DefaultValueSql: null,
                ProviderAnnotations: []);
    }
}
