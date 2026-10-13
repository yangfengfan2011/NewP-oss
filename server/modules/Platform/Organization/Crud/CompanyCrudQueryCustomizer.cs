using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class CompanyCrudQueryCustomizer : IStandardCrudQueryCustomizer
{
    public string ResourceKey => "organization.company";

    /// <summary>
    /// 把公司编码/名称关键词和启用状态转换为平台查询条件，实际筛选继续由数据库执行器下推。
    /// </summary>
    public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query)
    {
        _ = descriptor;
        var conditions = new List<StandardCrudQueryCondition>();
        if (query.Filters?.GetValueOrDefault("keyword") is { Length: > 0 } keyword)
        {
            conditions.Add(new StandardCrudQueryCondition("ECODE", "contains", keyword, "keyword", "or"));
            conditions.Add(new StandardCrudQueryCondition("ENAME", "contains", keyword, "keyword", "or"));
        }

        if (query.Filters?.GetValueOrDefault("isEnabled") is { Length: > 0 } isEnabled)
        {
            conditions.Add(new StandardCrudQueryCondition("IsEnabled", "=", isEnabled));
        }

        return conditions;
    }
}
