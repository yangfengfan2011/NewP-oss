using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudQueryCustomizer
{
    string ResourceKey { get; }

    /// <summary>
    /// 将模块私有查询参数转换为内核可验证的标准查询条件。
    /// </summary>
    IReadOnlyList<StandardCrudQueryCondition> BuildConditions(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query);
}
