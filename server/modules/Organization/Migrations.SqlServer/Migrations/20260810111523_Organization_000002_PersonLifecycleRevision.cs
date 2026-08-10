using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations;

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class OrganizationPersonLifecycleRevision : Migration
{
    public const string MigrationId = "20260810111523_Organization_000002_PersonLifecycleRevision";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "Revision",
            table: "Person",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
    }

    // 迁移 stream 只允许整体回退到匹配备份，不在运行时执行破坏性 Down。
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
