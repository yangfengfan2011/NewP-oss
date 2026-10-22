using Microsoft.EntityFrameworkCore;

namespace Partner.AssetCatalog.Migrations;

/// <summary>只用于外部 migration 的受支持 EF provider model，不提供运行时插件入口。</summary>
public sealed class AssetDbContext(DbContextOptions<AssetDbContext> options) : DbContext(options)
{
    // EF设计期与migration目标共用冻结模型，Host运行时仍消费自己的正式PersistencePlan。
    protected override void OnModelCreating(ModelBuilder modelBuilder) => BuildAssetModel(modelBuilder);

    /// <summary>冻结 Asset 初始 migration 的真实目标；固定字段与 authoring metadata 对齐。</summary>
    internal static void BuildAssetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.1");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 128);
        modelBuilder.UseIdentityColumns();
        var asset = modelBuilder.SharedTypeEntity("Asset", typeof(Dictionary<string, object?>));
        asset.ToTable("Asset", "np_partner_assetcatalog");
        asset.IndexerProperty<string>("EID").HasMaxLength(128).IsRequired();
        asset.IndexerProperty<string>("CreatedBy").HasMaxLength(128).IsRequired();
        asset.IndexerProperty<DateTime>("CreatedAt").IsRequired();
        asset.IndexerProperty<string>("UpdatedBy").HasMaxLength(128).IsRequired();
        asset.IndexerProperty<DateTime>("UpdatedAt").IsRequired();
        asset.IndexerProperty<string>("Code").HasMaxLength(256).IsRequired();
        asset.IndexerProperty<string>("Name").HasMaxLength(256).IsRequired();
        asset.IndexerProperty<DateOnly?>("PurchasedAt").HasColumnType("date");
        asset.HasKey("EID");
        asset.HasIndex("EID").HasDatabaseName("UX_Asset_EID").IsUnique();
        asset.HasIndex("Code").HasDatabaseName("UX_Asset_Code").IsUnique();
    }
}
