using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Ownership;

namespace ICP.Platform.Modules.Document;

/// <summary>独立文档维护只绑定本模块正式对象资源，不涉及文件或接收账号目标。</summary>
public sealed class DocumentOwnershipInitializationButtonHandler(IStandardCrudResourceRegistry resources, EntityOwnershipMaintenance maintenance)
    : ObjectOwnershipInitializationButtonHandler("document.document", resources, maintenance);
