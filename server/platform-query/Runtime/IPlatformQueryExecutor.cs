using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;

namespace ICP.Platform.Query.Runtime;

/// <summary>
/// 平台查询内核统一入口。
/// 调用方只提供 descriptor 与标准化后的查询请求，具体 EF 执行、字段裁剪和稳定错误由内核负责。
/// </summary>
public interface IPlatformQueryExecutor
{
    /// <summary>
    /// 执行一次平台查询。
    /// </summary>
    /// <param name="descriptor">查询资源边界描述。</param>
    /// <param name="request">调用方的字段、分页、排序和条件意图。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>供上层 adapter 继续组装响应的查询结果。</returns>
    Task<PlatformQueryResult> QueryAsync(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request,
        CancellationToken cancellationToken = default);
}
