using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ICP.Platform.Modules.Identity.Migrations.SqlServer.Migrations;

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class IdentityFoundation : Migration
{
    public const string MigrationId = "20260810033908_Identity_000001_Foundation";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "dbo");

        migrationBuilder.CreateTable(
            name: "IdentityAccount",
            schema: "dbo",
            columns: table => new
            {
                EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ECODE = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ENAME = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                IsLocalAdministrator = table.Column<bool>(type: "bit", nullable: false),
                IsPersonnelBound = table.Column<bool>(type: "bit", nullable: false),
                NormalizedECODE = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                PersonnelDisableHold = table.Column<bool>(type: "bit", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_IdentityAccount", x => x.EID));

        migrationBuilder.CreateTable(
            name: "IdentitySecurityEvent",
            schema: "dbo",
            columns: table => new
            {
                EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ActorAccountEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ActorKind = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                EventType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                TargetAccountEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                TraceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IdentitySecurityEvent", x => x.EID);
                table.ForeignKey(
                    name: "FK_IdentitySecurityEvent_ActorIdentityAccount",
                    column: x => x.ActorAccountEID,
                    principalSchema: "dbo",
                    principalTable: "IdentityAccount",
                    principalColumn: "EID",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_IdentitySecurityEvent_TargetIdentityAccount",
                    column: x => x.TargetAccountEID,
                    principalSchema: "dbo",
                    principalTable: "IdentityAccount",
                    principalColumn: "EID",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LocalCredential",
            schema: "dbo",
            columns: table => new
            {
                EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                AccountEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                IsTemporary = table.Column<bool>(type: "bit", nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LocalCredential", x => x.EID);
                table.ForeignKey(
                    name: "FK_LocalCredential_IdentityAccount",
                    column: x => x.AccountEID,
                    principalSchema: "dbo",
                    principalTable: "IdentityAccount",
                    principalColumn: "EID",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IdentitySecurityEvent_ActorAccountEID",
            schema: "dbo",
            table: "IdentitySecurityEvent",
            column: "ActorAccountEID");
        migrationBuilder.CreateIndex(
            name: "IX_IdentitySecurityEvent_TargetAccount_OccurredAt",
            schema: "dbo",
            table: "IdentitySecurityEvent",
            columns: ["TargetAccountEID", "OccurredAt"]);
        migrationBuilder.CreateIndex(
            name: "UX_IdentitySecurityEvent_EID",
            schema: "dbo",
            table: "IdentitySecurityEvent",
            column: "EID",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_LocalCredential_EID",
            schema: "dbo",
            table: "LocalCredential",
            column: "EID",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_LocalCredential_AccountEID",
            schema: "dbo",
            table: "LocalCredential",
            column: "AccountEID",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_IdentityAccount_EID",
            schema: "dbo",
            table: "IdentityAccount",
            column: "EID",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_IdentityAccount_NormalizedECODE",
            schema: "dbo",
            table: "IdentityAccount",
            column: "NormalizedECODE",
            unique: true);
    }

    // 模块迁移仅允许 safe-expand；回退通过发布前数据库备份完成，不生成破坏性 Down 操作。
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
