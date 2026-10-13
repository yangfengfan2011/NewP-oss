using ICP.Platform.Organization.Contracts;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>业务引用种类到已登记的规范资源身份；Model 名称不能推导唯一资源。</summary>
internal static class OrganizationDisplayReadResources
{
    /// <summary>返回公司、部门、岗位引用的稳定读取目标。</summary>
    public static string For(OrganizationRecordKind kind) => kind switch
    {
        OrganizationRecordKind.Company => "organization.company",
        OrganizationRecordKind.Department => "organization.department",
        OrganizationRecordKind.Position => "organization.position",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported organization display target.")
    };
}
