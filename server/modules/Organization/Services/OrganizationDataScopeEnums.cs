using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>人员枚举条件使用 Organization 已发布的任职状态值。</summary>
public sealed class OrganizationDataScopeEnums : IDataScopeEnumFieldProvider
{
    public IReadOnlyList<DataScopeEnumField> Fields { get; } =
        [new("Organization/Person", "EmploymentStatus", Enum.GetNames<EmploymentStatus>())];
}
