using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PositionCrudQueryCustomizer : IStandardCrudQueryCustomizer
{
    public string ResourceKey => "organization.position";

    /// <summary>
    /// 把岗位名称/描述关键词与启用状态筛选转换为平台查询条件。
    /// </summary>
    public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query)
    {
        _ = descriptor;
        var conditions = new List<StandardCrudQueryCondition>();
        if (query.Filters?.GetValueOrDefault("keyword") is { Length: > 0 } keyword)
        {
            conditions.Add(new StandardCrudQueryCondition("ENAME", "contains", keyword, "keyword", "or"));
            conditions.Add(new StandardCrudQueryCondition("Description", "contains", keyword, "keyword", "or"));
        }

        if (query.Filters?.GetValueOrDefault("isEnabled") is { Length: > 0 } isEnabled)
        {
            conditions.Add(new StandardCrudQueryCondition("IsEnabled", "=", isEnabled));
        }

        return conditions;
    }
}
