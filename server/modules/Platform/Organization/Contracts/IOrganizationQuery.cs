namespace ICP.Platform.Organization.Contracts;

/// <summary>服务端内部组织查询入口；每次读取取得一份一致且不可变的当前结果，不能保存到登录快照。</summary>
public interface IOrganizationQuery
{
    /// <summary>取得当前组织视图；不可用时抛出异常，绝不以空结果或数据库回源代替。</summary>
    ValueTask<IOrganizationReadView> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>完整原始档案的类型；仅限 Organization 自有当前数据，不包含账号或授权事实。</summary>
public enum OrganizationRecordKind
{
    /// <summary>公司。</summary>
    Company,
    /// <summary>部门。</summary>
    Department,
    /// <summary>岗位。</summary>
    Position,
    /// <summary>人员。</summary>
    Person
}

/// <summary>同一次发布的只读查询视图；所有方法只按标识及关系索引读取，不执行数据库查询。</summary>
public interface IOrganizationReadView
{
    /// <summary>按标识取得完整当前原始字段（含审计、停用记录）；不存在时返回 null。调用方负责对外投影及授权。</summary>
    IReadOnlyDictionary<string, object?>? Find(OrganizationRecordKind kind, string id);

    /// <summary>按不区分大小写的公司业务编码查找完整档案；输入首尾空格被忽略。</summary>
    IReadOnlyDictionary<string, object?>? FindCompanyByCode(string code);

    /// <summary>按账号取得人员及当前公司、部门与从直接上级到根的完整链；未绑定返回 null，准备阶段组织字段为空。</summary>
    CurrentOrganization? FindByAccount(string accountId);

    /// <summary>批量解析指定标识的当前名称和自身启停状态；重复标识合并，不存在的标识省略。</summary>
    IReadOnlyDictionary<string, OrganizationName> ResolveNames(OrganizationRecordKind kind, IReadOnlyCollection<string> ids);
}

/// <summary>当前组织关系。只包含 Organization 自有事实；上级链不含所属部门，顺序为直接上级到根。</summary>
public sealed record CurrentOrganization(
    IReadOnlyDictionary<string, object?> Person,
    IReadOnlyDictionary<string, object?>? Company,
    IReadOnlyDictionary<string, object?>? Department,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Ancestors);

/// <summary>当前名称及对象自身启停状态；展示标记由消费者决定。</summary>
public sealed record OrganizationName(string Name, bool IsEnabled);
