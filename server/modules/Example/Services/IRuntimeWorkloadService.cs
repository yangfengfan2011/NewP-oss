using ICP.Platform.Modules.Example.DTOs;

namespace ICP.Platform.Modules.Example.Services;

public interface IRuntimeWorkloadService
{
    /// <summary>
    /// 按当前平台时间生成本周与下周的完整运行时列集及其不透明上下文令牌。
    /// </summary>
    /// <returns>可序列化的模块列 DTO 及与该周期绑定的不透明令牌。</returns>
    RuntimeWorkloadColumnSetDto GetColumns();

    /// <summary>
    /// 使用列阶段签发的上下文令牌计算并分页返回示例成员工作量。
    /// </summary>
    /// <param name="contextToken">列阶段签发且只由本模块解释的上下文令牌。</param>
    /// <param name="keyword">可选成员姓名筛选。</param>
    /// <param name="pageIndex">从一开始的页码。</param>
    /// <param name="pageSize">标准列表页大小。</param>
    /// <param name="sortBy">可选的当前列字段。</param>
    /// <param name="sortDirection">可选的升降序方向。</param>
    /// <returns>同一列周期内计算得到的分页行。</returns>
    RuntimeWorkloadListResultDto GetRows(
        string contextToken,
        string? keyword,
        int pageIndex,
        int pageSize,
        string? sortBy = null,
        string? sortDirection = null);
}
