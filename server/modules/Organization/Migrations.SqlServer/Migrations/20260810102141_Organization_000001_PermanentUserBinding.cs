using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations;

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class OrganizationPermanentUserBinding : Migration
{
    public const string MigrationId = "20260810102141_Organization_000001_PermanentUserBinding";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PersonUserLink")
            .Annotation("NewP:OwnerModuleId", "Organization");
        migrationBuilder.AddColumn<string>(name: "UserBindingReason", table: "Person", type: "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "UserBoundAt", table: "Person", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>(name: "UserBoundBy", table: "Person", type: "nvarchar(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<string>(name: "UserEID", table: "Person", type: "nvarchar(256)", maxLength: 256, nullable: true);
        migrationBuilder.CreateIndex(name: "UX_Person_UserEID", table: "Person", column: "UserEID", unique: true, filter: "[UserEID] IS NOT NULL");
    }

    // 该迁移永久丢弃旧绑定行；回退只能恢复与旧应用、旧 frontier 匹配的迁移前数据库备份。
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
