using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;

namespace ICP.Platform.Modules.Part;

/// <summary>独立Part真实路由固定所属业务资源，不另声明对象动作权限。</summary>
public sealed class PartOwnershipTransferButtonHandler(EntityOwnershipTransfer transfer, IAccountOwnershipDirectory accounts)
    : ObjectOwnershipTransferButtonHandler("part.part", transfer, accounts);
