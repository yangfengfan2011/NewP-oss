using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Partner.AssetCatalog.Migrations;

/// <summary>Asset 的真实初始 schema；Host 是唯一 Up 执行者。</summary>
[DbContext(typeof(AssetDbContext))]
[Migration("20261010000100_AssetInitial")]
public sealed class AssetInitial : Migration
{
    // 初始DDL按正式ProviderTarget的逻辑列序声明；固定字段不从Blueprint的显示字段猜测。
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("np_partner_assetcatalog");
        migrationBuilder.CreateTable("Asset", schema: "np_partner_assetcatalog", columns: table => new
        {
            EID = table.Column<string>("nvarchar(128)", maxLength: 128, nullable: false),
            Code = table.Column<string>("nvarchar(256)", maxLength: 256, nullable: false),
            CreatedAt = table.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = table.Column<string>("nvarchar(128)", maxLength: 128, nullable: false),
            Name = table.Column<string>("nvarchar(256)", maxLength: 256, nullable: false),
            PurchasedAt = table.Column<DateOnly>("date", nullable: true),
            UpdatedAt = table.Column<DateTime>("datetime2", nullable: false),
            UpdatedBy = table.Column<string>("nvarchar(128)", maxLength: 128, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Asset", row => row.EID));
        migrationBuilder.CreateIndex("UX_Asset_EID", "Asset", "EID", "np_partner_assetcatalog", unique: true);
        migrationBuilder.CreateIndex("UX_Asset_Code", "Asset", "Code", "np_partner_assetcatalog", unique: true);
    }

    // 仅定义人工回退；正式Host从不自动调用Down。
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Asset", "np_partner_assetcatalog");

    // EF bundle的target与设计期上下文保持一致，不生成另一套运行时模型。
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AssetDbContext.BuildAssetModel(modelBuilder);
}
