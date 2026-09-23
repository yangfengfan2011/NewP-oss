using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Identity.Contracts;

namespace ICP.Platform.Modules.Identity;

/// <summary>账号枚举条件使用 Identity 已发布的账号状态值。</summary>
public sealed class IdentityDataScopeEnums : IDataScopeEnumFieldProvider
{
    public IReadOnlyList<DataScopeEnumField> Fields { get; } =
        [new("Identity/IdentityAccount", "Status", Enum.GetNames<IdentityAccountStatus>())];
}
