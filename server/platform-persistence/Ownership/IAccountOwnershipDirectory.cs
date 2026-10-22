namespace ICP.Platform.Persistence.Ownership;

/// <summary>提供对象归属所需的可信账号及当前直接部门，不向消费者暴露人员档案。</summary>
public interface IAccountOwnershipDirectory
{
    /// <summary>批量查询当前账号事实；不存在或已删除的账号省略，未具备部门归属时 DepartmentId 为空。</summary>
    ValueTask<IReadOnlyDictionary<string, AccountOwnership>> FindManyAsync(
        IReadOnlyCollection<string> accountIds, CancellationToken cancellationToken = default);

    /// <summary>批量读取可显示部门名称，不可读部门使用受控遮蔽值，缺失省略，目录故障继续传播。</summary>
    ValueTask<IReadOnlyDictionary<string, string>> FindDepartmentNamesAsync(
        IReadOnlyCollection<string> departmentIds, CancellationToken cancellationToken = default);

    /// <summary>按受控查询搜索一个账号候选片段，未具备直接部门的账号仍明确返回空部门供界面解释。</summary>
    ValueTask<IReadOnlyList<AccountOwnershipCandidate>> SearchAsync(
        string? query, CancellationToken cancellationToken = default);
}

/// <summary>对象初始化时观察到的账号与当前直接部门；并不声称还原创建时组织历史。</summary>
public sealed record AccountOwnership(string AccountId, string? DepartmentId);

/// <summary>受控人工补齐选择使用的最小账号展示，不包含人员档案。</summary>
public sealed record AccountOwnershipCandidate(string AccountId, string Code, string Name, string? DepartmentId);
