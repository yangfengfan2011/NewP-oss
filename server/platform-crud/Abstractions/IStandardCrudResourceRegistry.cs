using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudResourceRegistry
{
    /// <summary>
    /// 返回所有已显式注册的标准 CRUD 资源；返回集合只读，避免运行期修改路由真源。
    /// </summary>
    IReadOnlyList<StandardCrudDescriptor> GetAll();

    /// <summary>
    /// 按稳定资源 key 查找 descriptor；未注册时返回 null，由调用方决定是否 fail closed。
    /// </summary>
    StandardCrudDescriptor? FindByResourceKey(string resourceKey);

    /// <summary>
    /// 按标准 CRUD 业务路由查找 descriptor；route 会先规范化，避免斜杠差异形成双真源。
    /// </summary>
    StandardCrudDescriptor? FindByRoute(string route);
}
