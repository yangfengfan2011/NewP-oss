using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudSummaryProvider
{
    string ResourceKey { get; }
    string Key { get; }

    /// <summary>
    /// 基于详情字段生成摘要块；失败或未配置信号由实现返回稳定状态。
    /// </summary>
    Task<StandardCrudSummaryBlock> BuildAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default);
}
