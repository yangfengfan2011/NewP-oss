using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;

namespace ICP.Platform.Modules.Organization.Directories;

/// <summary>
/// 通过平台查询内核批量读取全局岗位，避免人员列表对岗位表逐行查询。
/// </summary>
/// <param name="queryExecutor">平台查询内核统一执行入口。</param>
/// <param name="resourceRegistry">已验证且冻结的标准资源 descriptor 注册表。</param>
public sealed class PlatformQueryPositionDirectory(
    IPlatformQueryExecutor queryExecutor,
    IStandardCrudResourceRegistry resourceRegistry) : IPositionDirectory
{
    /// <inheritdoc/>
    public async Task<Position?> GetByIdAsync(
        string positionId,
        CancellationToken cancellationToken = default)
    {
        var positions = await GetByIdsAsync([positionId], cancellationToken);
        return positions.GetValueOrDefault(positionId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, Position>> GetByIdsAsync(
        IReadOnlyCollection<string> positionIds,
        CancellationToken cancellationToken = default)
    {
        var ids = positionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, Position>(StringComparer.Ordinal);
        }

        var descriptor = resourceRegistry.FindByResourceKey("organization.position")
            ?? throw new InvalidOperationException("Organization position descriptor is not registered.");
        var queryDescriptor = StandardCrudQueryDescriptorAdapter.ToQueryDescriptor(descriptor);
        var conditions = ids
            .Select(id => new PlatformQueryCondition("EID", "=", id, "positionIds", "or"))
            .ToArray();
        var result = await queryExecutor.QueryAsync(
            queryDescriptor,
            new PlatformQueryRequest(
                ["EID", "ENAME", "Description", "IsEnabled"],
                Page: 1,
                PageSize: ids.Length,
                SortBy: "ENAME",
                SortDirection: "asc",
                Conditions: conditions),
            cancellationToken);

        return result.Items
            .Select(Map)
            .ToDictionary(position => position.Id, StringComparer.Ordinal);
    }

    // 目录只投影人员校验和显示所需字段，不暴露 Standard CRUD 行字典。
    private static Position Map(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = ReadString(row, "EID"),
        Name = ReadString(row, "ENAME"),
        Description = row.GetValueOrDefault("Description") as string,
        IsEnabled = row.GetValueOrDefault("IsEnabled") is true
    };

    // 平台查询结果已经过 descriptor 字段规划；这里仅把必需标识和值收敛为目录模型。
    private static string ReadString(IReadOnlyDictionary<string, object?> row, string field) =>
        row.GetValueOrDefault(field)?.ToString() ?? string.Empty;
}
