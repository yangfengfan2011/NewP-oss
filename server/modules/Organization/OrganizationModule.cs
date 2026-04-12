using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Sdk;

namespace ICP.Platform.Modules.Organization;

public sealed class OrganizationModule : ModuleBase, IPermissionProvider
{
    public override string ModuleId => "Organization";

    public override string DisplayName => "组织结构";

    public override string Description => "组织结构管理模块";

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "Organization.Person.View",
                DisplayName = "查看人员",
                Description = "允许查看组织结构中的人员信息",
                ModuleId = ModuleId,
                Group = "人员"
            }
        };
    }
}
