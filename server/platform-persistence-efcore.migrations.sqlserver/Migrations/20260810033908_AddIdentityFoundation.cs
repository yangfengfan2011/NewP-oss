using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "User",
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
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.EID);
                });

            migrationBuilder.CreateTable(
                name: "AuthenticationSession",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevocationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SessionType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticationSession", x => x.EID);
                    table.ForeignKey(
                        name: "FK_AuthenticationSession_User",
                        column: x => x.UserEID,
                        principalSchema: "dbo",
                        principalTable: "User",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentitySecurityEvent",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorKind = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActorUserEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetUserEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TraceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentitySecurityEvent", x => x.EID);
                    table.ForeignKey(
                        name: "FK_IdentitySecurityEvent_ActorUser",
                        column: x => x.ActorUserEID,
                        principalSchema: "dbo",
                        principalTable: "User",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdentitySecurityEvent_TargetUser",
                        column: x => x.TargetUserEID,
                        principalSchema: "dbo",
                        principalTable: "User",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocalCredential",
                schema: "dbo",
                columns: table => new
                {
                    EID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsTemporary = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserEID = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalCredential", x => x.EID);
                    table.ForeignKey(
                        name: "FK_LocalCredential_User",
                        column: x => x.UserEID,
                        principalSchema: "dbo",
                        principalTable: "User",
                        principalColumn: "EID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSession_ExpiresAt",
                schema: "dbo",
                table: "AuthenticationSession",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSession_UserEID",
                schema: "dbo",
                table: "AuthenticationSession",
                column: "UserEID");

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

            migrationBuilder.CreateIndex(
                name: "IX_IdentitySecurityEvent_ActorUserEID",
                schema: "dbo",
                table: "IdentitySecurityEvent",
                column: "ActorUserEID");

            migrationBuilder.CreateIndex(
                name: "IX_IdentitySecurityEvent_TargetUser_OccurredAt",
                schema: "dbo",
                table: "IdentitySecurityEvent",
                columns: new[] { "TargetUserEID", "OccurredAt" });

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
                name: "UX_LocalCredential_UserEID",
                schema: "dbo",
                table: "LocalCredential",
                column: "UserEID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_User_EID",
                schema: "dbo",
                table: "User",
                column: "EID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_User_NormalizedECODE",
                schema: "dbo",
                table: "User",
                column: "NormalizedECODE",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticationSession",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "IdentitySecurityEvent",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "LocalCredential",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "User",
                schema: "dbo");
        }
    }
}
