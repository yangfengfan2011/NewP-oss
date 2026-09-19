using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Runtime;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 让无专用动作实现的标准 CRUD 对象直接复用公共创建、更新和删除行为，无需声明空派生类。
/// </summary>
internal sealed class StandardCrudObjectBehavior : ObjectBehaviorBase
{
    // 每个注册 identity 只保存静态声明和同作用域默认写入模块，不持有操作可变状态。
    internal StandardCrudObjectBehavior(
        ObjectBehaviorDefinition definition,
        IStandardCrudResourceRegistry resources,
        StandardCrudDefaultWriter defaultWriter)
        : base(definition, resources, defaultWriter)
    {
    }
}
