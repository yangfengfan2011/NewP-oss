using Microsoft.EntityFrameworkCore.Migrations;

using ICP.Platform.Persistence.ProviderTargets;

#nullable disable

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Organization_000005_RemovePersonRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "dbo",
                table: "Person")
                .Annotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeColumn(
                        "Organization",
                        17,
                        RequiredLongColumn("Revision")));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 人员版本值删除后无法恢复其旧语义；回退必须使用删除列前的已验证备份。
            throw new NotSupportedException(
                "Migration 20260902135016_Organization_000005_RemovePersonRevision is backup-restore-only; restore a verified database backup instead of running Down.");
        }

        private static ProviderColumnTarget RequiredLongColumn(string name) =>
            new(
                name,
                "bigint",
                typeof(long).FullName!,
                IsNullable: false,
                MaxLength: null,
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
