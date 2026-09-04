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

    public const string CompanyStem = "Organization.Company";
    public const string CompanyView = CompanyStem + ".View";
    public const string CompanyCreate = CompanyStem + ".Create";
    public const string CompanyUpdate = CompanyStem + ".Update";
    public const string CompanyDelete = CompanyStem + ".Delete";

    public const string DepartmentStem = "Organization.Department";
    public const string DepartmentView = DepartmentStem + ".View";
    public const string DepartmentCreate = DepartmentStem + ".Create";
    public const string DepartmentUpdate = DepartmentStem + ".Update";
    public const string DepartmentDelete = DepartmentStem + ".Delete";

    public const string PositionStem = "Organization.Position";
    public const string PositionView = PositionStem + ".View";
    public const string PositionCreate = PositionStem + ".Create";
    public const string PositionUpdate = PositionStem + ".Update";
}
