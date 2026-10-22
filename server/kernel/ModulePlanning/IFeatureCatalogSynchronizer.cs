namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>在完整运行时候选准备成功后，将已验证的功能目录同步到授权模块。</summary>
public interface IFeatureCatalogSynchronizer
{
    /// <summary>事务提交全部目录索引和初始分配后发布活动快照；失败不发布部分结果。</summary>
    Task SynchronizeAsync(IReadOnlyList<FeaturePoint> points, CancellationToken cancellationToken = default);
}
