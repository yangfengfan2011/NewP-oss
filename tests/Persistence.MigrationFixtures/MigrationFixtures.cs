using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ICP.Platform.Persistence.Tests;

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class ForbiddenSqlMigration : Migration
{
    public const string MigrationId = "20260809999900_ForbiddenSql";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("SELECT 1;");
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class TransactionSuppressedSqlMigration : Migration
{
    public const string MigrationId = "20260809999901_TransactionSuppressedSql";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("SELECT 1;", suppressTransaction: true);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class MalformedCreateTableMigration : Migration
{
    public const string MigrationId = "20260812000100_MalformedCreateTable";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
        name: "Asset",
        schema: "np_partner_assetcatalog",
        columns: table => new
        {
            Id = table.Column<int>(type: "int", nullable: false)
        },
        constraints: table => table.PrimaryKey("PK_Asset", asset => asset.Id));

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class MalformedRenameTableMigration : Migration
{
    public const string MigrationId = "20260812000200_MalformedRenameTable";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.RenameTable(
        name: "OldAsset",
        schema: "np_partner_assetcatalog",
        newName: "UnexpectedAsset");

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class MalformedAlterColumnMigration : Migration
{
    public const string MigrationId = "20260812000300_MalformedAlterColumn";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AlterColumn<int>(
        name: "Id",
        schema: "np_partner_assetcatalog",
        table: "Asset",
        type: "int",
        nullable: false,
        oldClrType: typeof(string),
        oldType: "nvarchar(128)",
        oldMaxLength: 128);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class MalformedDropColumnMigration : Migration
{
    public const string MigrationId = "20260812000400_MalformedDropColumn";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "Obsolete",
        schema: "np_partner_assetcatalog",
        table: "Asset");

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class FilteredIndexMigration : Migration
{
    public const string MigrationId = "20260809999800_FilteredIndex";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
        name: "IX_Asset_Filtered", schema: "np_partner_assetcatalog", table: "Asset", column: "Id", filter: "[Id] <> N''");
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class UnfilteredIndexMigration : Migration
{
    public const string MigrationId = "20260809999600_UnfilteredIndex";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
        name: "IX_Asset_Unfiltered", schema: "np_partner_assetcatalog", table: "Asset", column: "Id");
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class UnfilteredUniqueIndexMigration : Migration
{
    public const string MigrationId = "20260809999500_UnfilteredUniqueIndex";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
        name: "UX_Asset_Id", schema: "np_partner_assetcatalog", table: "Asset", column: "Id", unique: true);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class DescendingIndexMigration : Migration
{
    public const string MigrationId = "20260809999300_DescendingIndex";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
        name: "IX_Asset_Unfiltered", schema: "np_partner_assetcatalog", table: "Asset", column: "Id", descending: [true]);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class ClusteredIndexMigration : Migration
{
    public const string MigrationId = "20260809999200_ClusteredIndex";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
            name: "IX_Asset_Unfiltered", schema: "np_partner_assetcatalog", table: "Asset", column: "Id")
        .Annotation("SqlServer:Clustered", true);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class IncludedColumnsIndexMigration : Migration
{
    public const string MigrationId = "20260809999100_IncludedColumnsIndex";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
            name: "IX_Asset_Unfiltered", schema: "np_partner_assetcatalog", table: "Asset", column: "Id")
        .Annotation("SqlServer:Include", new[] { "ENAME" });
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class SelfReportedLegacyDropMigration : Migration
{
    public const string MigrationId = "20260809999400_SelfReportedLegacyDrop";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "LegacyAsset", schema: "np_partner_assetcatalog")
            .Annotation("NewP:OwnerModuleId", "Partner.AssetCatalog");
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class TrustedOrganizationLegacyDropMigration : Migration
{
    public const string MigrationId = "20260810102141_Organization_000001_PermanentUserBinding";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "PersonUserLink");
    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class DeclaredForeignKeyMigration : Migration
{
    public const string MigrationId = "20260809999700_DeclaredForeignKey";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Parent",
            schema: "np_partner_assetcatalog",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Parent", x => x.Id));
        migrationBuilder.CreateTable(
            name: "Child",
            schema: "np_partner_assetcatalog",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ParentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Child", x => x.Id);
                table.ForeignKey(
                    name: "FK_Child_Parent",
                    column: x => x.ParentId,
                    principalSchema: "np_partner_assetcatalog",
                    principalTable: "Parent",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_Child_ParentId",
            schema: "np_partner_assetcatalog",
            table: "Child",
            column: "ParentId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class CreateAssetWithDownMigration : Migration
{
    public const string MigrationId = "20260812010000_CreateAssetWithDown";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("np_partner_assetcatalog");
        migrationBuilder.CreateTable(
            name: "Asset",
            schema: "np_partner_assetcatalog",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Asset", asset => asset.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "Asset", schema: "np_partner_assetcatalog");
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class CreateAssetCatalogMigration : Migration
{
    public const string MigrationId = "20260809001000_CreateAssetCatalog";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("np_partner_assetcatalog");
        migrationBuilder.CreateTable(
            name: "Asset",
            schema: "np_partner_assetcatalog",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Code = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Asset", asset => asset.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AddUniqueAssetCodeMigration : Migration
{
    public const string MigrationId = "20260809002000_AddUniqueAssetCode";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "UX_Asset_Code",
            schema: "np_partner_assetcatalog",
            table: "Asset",
            column: "Code",
            unique: true);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AddMismatchedRevisionColumnMigration : Migration
{
    public const string MigrationId = "20260812000100_AddMismatchedRevisionColumn";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>(
            name: "Revision",
            schema: "np_partner_assetcatalog",
            table: "Asset",
            type: "int",
            nullable: false,
            defaultValue: 0);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AddRevisionWithBusinessDefaultMigration : Migration
{
    public const string MigrationId = "20260812000200_AddRevisionWithBusinessDefault";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<long>(
            name: "Revision",
            schema: "np_partner_assetcatalog",
            table: "Asset",
            type: "bigint",
            nullable: false,
            defaultValue: 42L);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AddRevisionWithClrDefaultMigration : Migration
{
    public const string MigrationId = "20260812000300_AddRevisionWithClrDefault";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<long>(
            name: "Revision",
            schema: "np_partner_assetcatalog",
            table: "Asset",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class CreateProviderTransitionAssetMigration : Migration
{
    public const string MigrationId = "20260812001000_CreateProviderTransitionAsset";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("np_partner_transition");
        migrationBuilder.CreateTable(
            name: "TransitionAsset",
            schema: "np_partner_transition",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OptionalValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_TransitionAsset", asset => asset.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AddNullableTransitionColumnMigration : Migration
{
    public const string MigrationId = "20260812002000_AddNullableTransitionColumn";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "OptionalAdded",
            schema: "np_partner_transition",
            table: "TransitionAsset",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AddRequiredTransitionColumnMigration : Migration
{
    public const string MigrationId = "20260812003000_AddRequiredTransitionColumn";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<long>(
            name: "RequiredAdded",
            schema: "np_partner_transition",
            table: "TransitionAsset",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class RequireTransitionValueMigration : Migration
{
    public const string MigrationId = "20260812004000_RequireTransitionValue";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(
            name: "OptionalValue",
            schema: "np_partner_transition",
            table: "TransitionAsset",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(256)",
            oldMaxLength: 256,
            oldNullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) { }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class CreateFullUpAssetMigration : Migration
{
    public const string MigrationId = "20260812011000_CreateFullUpAsset";
    public const string OwnerModuleId = "Partner.FullUp";
    public const string Schema = "np_partner_fullup";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(Schema);
        migrationBuilder.CreateTable(
            name: "Parent",
            schema: Schema,
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Parent", parent => parent.Id));
        migrationBuilder.CreateTable(
            name: "RetiredAsset",
            schema: Schema,
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_RetiredAsset", asset => asset.Id));
        migrationBuilder.CreateTable(
            name: "LegacyAsset",
            schema: Schema,
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                LegacyCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ParentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Obsolete = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LegacyAsset", asset => asset.Id);
                table.ForeignKey(
                    name: "FK_LegacyAsset_Parent",
                    column: asset => asset.ParentId,
                    principalSchema: Schema,
                    principalTable: "Parent",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_LegacyAsset_LegacyCode",
            schema: Schema,
            table: "LegacyAsset",
            column: "LegacyCode");
        migrationBuilder.CreateIndex(
            name: "UX_LegacyAsset_ParentId",
            schema: Schema,
            table: "LegacyAsset",
            column: "ParentId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LegacyAsset", schema: Schema);
        migrationBuilder.DropTable(name: "RetiredAsset", schema: Schema);
        migrationBuilder.DropTable(name: "Parent", schema: Schema);
    }
}

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class TransformFullUpAssetMigration : Migration
{
    public const string MigrationId = "20260812012000_TransformFullUpAsset";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
                name: "FK_LegacyAsset_Parent",
                schema: CreateFullUpAssetMigration.Schema,
                table: "LegacyAsset")
            .Annotation(
                ProviderTransitionAnnotationNames.FromDescriptorV1,
                ProviderTransitionDescriptorCodec.EncodeForeignKey(
                    CreateFullUpAssetMigration.OwnerModuleId,
                    new ProviderForeignKeyTarget(
                        "FK_LegacyAsset_Parent",
                        ["ParentId"],
                        CreateFullUpAssetMigration.OwnerModuleId,
                        CreateFullUpAssetMigration.Schema,
                        "Parent",
                        ["Id"],
                        "Restrict")));
        migrationBuilder.DropIndex(
                name: "UX_LegacyAsset_ParentId",
                schema: CreateFullUpAssetMigration.Schema,
                table: "LegacyAsset")
            .Annotation(
                ProviderTransitionAnnotationNames.FromDescriptorV1,
                ProviderTransitionDescriptorCodec.EncodeIndex(
                    CreateFullUpAssetMigration.OwnerModuleId,
                    new ProviderIndexTarget(
                        "UX_LegacyAsset_ParentId",
                        ["ParentId"],
                        IsUnique: true,
                        IsDescending: null,
                        Filter: null,
                        ProviderAnnotations: [])));
        migrationBuilder.DropPrimaryKey(
                name: "PK_LegacyAsset",
                schema: CreateFullUpAssetMigration.Schema,
                table: "LegacyAsset")
            .Annotation(
                ProviderTransitionAnnotationNames.FromDescriptorV1,
                ProviderTransitionDescriptorCodec.EncodeKey(
                    CreateFullUpAssetMigration.OwnerModuleId,
                    new ProviderKeyTarget("PK_LegacyAsset", ["Id"])));
        migrationBuilder.DropColumn(
                name: "Obsolete",
                schema: CreateFullUpAssetMigration.Schema,
                table: "LegacyAsset")
            .Annotation(
                ProviderTransitionAnnotationNames.FromDescriptorV1,
                ProviderTransitionDescriptorCodec.EncodeColumn(
                    CreateFullUpAssetMigration.OwnerModuleId,
                    3,
                    StringColumn("Obsolete", "nvarchar(256)", 256, isNullable: true)));
        migrationBuilder.DropTable(
                name: "RetiredAsset",
                schema: CreateFullUpAssetMigration.Schema)
            .Annotation(
                ProviderTransitionAnnotationNames.FromDescriptorV1,
                ProviderTransitionDescriptorCodec.EncodeTable(
                    CreateFullUpAssetMigration.OwnerModuleId,
                    new ProviderTableTarget(
                        CreateFullUpAssetMigration.OwnerModuleId,
                        CreateFullUpAssetMigration.Schema,
                        "RetiredAsset",
                        [StringColumn("Id", "nvarchar(128)", 128, isNullable: false)],
                        new ProviderKeyTarget("PK_RetiredAsset", ["Id"]),
                        [],
                        [],
                        [],
                        [])));
        migrationBuilder.RenameTable(
            name: "LegacyAsset",
            schema: CreateFullUpAssetMigration.Schema,
            newName: "Asset",
            newSchema: CreateFullUpAssetMigration.Schema);
        migrationBuilder.RenameColumn(
            name: "LegacyCode",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset",
            newName: "Code");
        migrationBuilder.RenameIndex(
            name: "IX_LegacyAsset_LegacyCode",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset",
            newName: "IX_Asset_Code");
        migrationBuilder.AlterColumn<string>(
            name: "Code",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128,
            oldNullable: true);
        migrationBuilder.AddPrimaryKey(
            name: "PK_Asset",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset",
            column: "Id");
        migrationBuilder.AddForeignKey(
            name: "FK_Asset_Parent",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset",
            column: "ParentId",
            principalSchema: CreateFullUpAssetMigration.Schema,
            principalTable: "Parent",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex(
            name: "UX_Asset_ParentId",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset",
            column: "ParentId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "Asset", schema: CreateFullUpAssetMigration.Schema);

    private static ProviderColumnTarget StringColumn(
        string name,
        string storeType,
        int maxLength,
        bool isNullable) =>
        new(
            name,
            storeType,
            typeof(string).FullName!,
            isNullable,
            maxLength,
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

[DbContext(typeof(EfPlatformDbContext))]
[Migration(MigrationId)]
public sealed class AddUniqueFullUpAssetCodeMigration : Migration
{
    public const string MigrationId = "20260812013000_AddUniqueFullUpAssetCode";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
                name: "IX_Asset_Code",
                schema: CreateFullUpAssetMigration.Schema,
                table: "Asset")
            .Annotation(
                ProviderTransitionAnnotationNames.FromDescriptorV1,
                ProviderTransitionDescriptorCodec.EncodeIndex(
                    CreateFullUpAssetMigration.OwnerModuleId,
                    new ProviderIndexTarget(
                        "IX_Asset_Code",
                        ["Code"],
                        IsUnique: false,
                        IsDescending: null,
                        Filter: null,
                        ProviderAnnotations: [])));
        migrationBuilder.CreateIndex(
            name: "UX_Asset_Code",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset",
            column: "Code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_Asset_Code",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset");
        migrationBuilder.CreateIndex(
            name: "IX_Asset_Code",
            schema: CreateFullUpAssetMigration.Schema,
            table: "Asset",
            column: "Code");
    }
}
