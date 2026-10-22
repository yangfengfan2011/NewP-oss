using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Directories;

/// <summary>
/// 为人员校验与显示投影提供岗位的窄读取目录。
/// </summary>
public interface IPositionDirectory
{
    /// <summary>
    /// 按岗位标识读取当前目录项；不存在时返回 null。
    /// </summary>
    Task<Position?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量读取岗位目录项并按标识索引，调用方可用一次查询完成一页人员显示投影。
    /// </summary>
    Task<IReadOnlyDictionary<string, Position>> GetByIdsAsync(
        IReadOnlyCollection<string> positionIds,
        CancellationToken cancellationToken = default);
}
