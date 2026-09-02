namespace ICP.Platform.Modules.Organization;

/// <summary>
/// Organization 模块拥有的稳定权限代码与 Standard CRUD stem。
/// </summary>
public static class OrganizationPermissions
{
    public const string PersonStem = "Organization.Person";
    public const string PersonView = PersonStem + ".View";
    public const string PersonCreate = PersonStem + ".Create";
    public const string PersonUpdate = PersonStem + ".Update";
    public const string PersonDelete = PersonStem + ".Delete";
    public const string PersonLinkUser = PersonStem + ".LinkUser";

    public const string CompanyView = "Organization.Company.View";
    public const string CompanyCreate = "Organization.Company.Create";
    public const string CompanyUpdate = "Organization.Company.Update";

    public const string DepartmentView = "Organization.Department.View";
    public const string DepartmentCreate = "Organization.Department.Create";
    public const string DepartmentUpdate = "Organization.Department.Update";

    public const string PositionStem = "Organization.Position";
    public const string PositionView = PositionStem + ".View";
    public const string PositionCreate = PositionStem + ".Create";
    public const string PositionUpdate = PositionStem + ".Update";
}
