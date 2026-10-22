using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;

namespace ICP.Platform.Modules.Document;

/// <summary>独立Document真实路由固定所属业务资源，不另声明对象动作权限。</summary>
public sealed class DocumentOwnershipTransferButtonHandler(EntityOwnershipTransfer transfer, IAccountOwnershipDirectory accounts)
    : ObjectOwnershipTransferButtonHandler("document.document", transfer, accounts);
