using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Ownership;

namespace ICP.Platform.Modules.Part;

/// <summary>独立零组件维护只绑定本模块正式对象资源，不创建额外权限目标。</summary>
public sealed class PartOwnershipInitializationButtonHandler(IStandardCrudResourceRegistry resources, EntityOwnershipMaintenance maintenance)
    : ObjectOwnershipInitializationButtonHandler("part.part", resources, maintenance);
