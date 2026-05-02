using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public static class PersonCrudDescriptorFactory
{
    /// <summary>
    /// 创建人员标准 CRUD 描述符；这里只声明字段、扩展点和删除策略，执行流程由 PlatformCrud 内核负责。
    /// </summary>
    public static StandardCrudDescriptor Create() => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled", "CreatedAt"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"]),
        OptionLabelFields = ["PersonNo", "ENAME"],
        SummaryBlockKeys = ["userLink", "authorizationSummary"],
        DisplayValueFields = ["CompanyName", "DepartmentName", "PositionName"],
        DeletePolicy = StandardCrudDeletePolicy.Conditional
    };
}
