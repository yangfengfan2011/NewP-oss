using System.Collections.Frozen;
using ICP.Platform.Kernel.ModulePlanning;
namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>提交后发布的目录，数据库 EID 只作为关联键，身份始终是完整位置。</summary>
public sealed class ActiveFeatureCatalog
{
    private FeatureCatalogSnapshot? _snapshot;
    public FeatureCatalogSnapshot Snapshot => Volatile.Read(ref _snapshot)
        ?? throw new InvalidOperationException("活动功能目录尚未发布。");
    /// <summary>同步完成后原子发布全部目录，不接受部分索引。</summary>
    public void Publish(IReadOnlyList<FeaturePoint> points, IReadOnlyDictionary<string, string> ids)
    {
        var indexed = points.Select(point => new IndexedFeature(point, ids.TryGetValue(point.Position, out var id)
            ? id : throw new InvalidOperationException("功能索引同步不完整。"))).ToArray();
        var snapshot = new FeatureCatalogSnapshot(indexed.ToFrozenDictionary(p => p.Point.Position, StringComparer.Ordinal),
            indexed.ToFrozenDictionary(p => p.Id, StringComparer.Ordinal), points.ToArray());
        Volatile.Write(ref _snapshot, snapshot);
    }
}
public sealed record IndexedFeature(FeaturePoint Point, string Id);
public sealed record FeatureCatalogSnapshot(IReadOnlyDictionary<string, IndexedFeature> ByPosition,
    IReadOnlyDictionary<string, IndexedFeature> ById, IReadOnlyList<FeaturePoint> Points);
