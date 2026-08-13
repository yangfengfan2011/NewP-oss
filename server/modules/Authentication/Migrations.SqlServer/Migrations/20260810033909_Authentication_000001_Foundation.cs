using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ICP.Platform.Modules.Authentication.Migrations.SqlServer.Migrations;

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AuthenticationFoundation : Migration
{
    public const string MigrationId = "20260810033909_Authentication_000001_Foundation";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "dbo");
        migrationBuilder.CreateTable(
            name: "AuthenticationSession",
            schema: "dbo",
            columns: table => new
            {
                EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                AccountEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                RevocationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                SessionType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                TokenHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuthenticationSession", x => x.EID);
                table.ForeignKey(
                    name: "FK_AuthenticationSession_IdentityAccount",
                    column: x => x.AccountEID,
                    principalSchema: "dbo",
                    principalTable: "IdentityAccount",
                    principalColumn: "EID",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_AuthenticationSession_ExpiresAt",
            schema: "dbo",
            table: "AuthenticationSession",
            column: "ExpiresAt");
        migrationBuilder.CreateIndex(
            name: "IX_AuthenticationSession_AccountEID",
            schema: "dbo",
            table: "AuthenticationSession",
            column: "AccountEID");
        migrationBuilder.CreateIndex(
            name: "UX_AuthenticationSession_EID",
            schema: "dbo",
            table: "AuthenticationSession",
            column: "EID",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_AuthenticationSession_TokenHash",
            schema: "dbo",
            table: "AuthenticationSession",
            column: "TokenHash",
            unique: true);
    }

    // 模块迁移仅允许 safe-expand；回退通过发布前数据库备份完成，不生成破坏性 Down 操作。
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
