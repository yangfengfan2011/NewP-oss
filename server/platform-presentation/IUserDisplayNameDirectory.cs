namespace ICP.Platform.Presentation;

/// <summary>仅暴露稳定账号 ID 对应的当前显示名称，不授予账号管理或其他快照读取能力。</summary>
public interface IUserDisplayNameDirectory
{
    /// <summary>批量读取当前名称；未命中的 ID 不出现在结果中，目录故障抛出异常，取消继续传播。</summary>
    Task<IReadOnlyDictionary<string, string>> FindManyAsync(
        IReadOnlyCollection<string> accountIds,
        CancellationToken cancellationToken = default);
}
