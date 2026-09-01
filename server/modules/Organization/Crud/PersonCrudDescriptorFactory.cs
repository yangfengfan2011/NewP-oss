using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Query.Descriptors;

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
        SortableFields = ["PersonNo", "ENAME", "EmploymentStatus", "IsEnabled"],
        DefaultSortField = "PersonNo",
        DefaultSortDirection = "asc",
        Authorization = StandardCrudAuthorization.ByConvention(OrganizationPermissions.PersonStem),
        EnabledField = "IsEnabled",
        ConcurrencyRevisionField = "Revision",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled", "Revision", "AccountEID"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled", "Revision", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt", "AccountEID", "AccountBoundAt", "AccountBoundBy", "AccountBindingReason"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "UpdatedAt"]),
        OptionLabelFields = ["PersonNo", "ENAME"],
        SummaryBlockKeys = ["userBinding", "authorizationSummary"],
        DisplayValueFields = BuildDisplayValueFields(),
        DeletePolicy = StandardCrudDeletePolicy.Conditional,
        RequiredFields =
        [
            new StandardCrudRequiredField
            {
                Field = "PersonNo",
                Scope = StandardCrudRequiredFieldScope.CreateOnly,
                ErrorCode = "Organization.Person.PersonNoRequired",
                Message = "人员工号不能为空。",
                ReasonKey = "organization.person.personNoRequired"
            },
            new StandardCrudRequiredField
            {
                Field = "ENAME",
                Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                ErrorCode = "Organization.Person.NameRequired",
                Message = "人员姓名不能为空。",
                ReasonKey = "organization.person.nameRequired"
            },
            new StandardCrudRequiredField
            {
                Field = "IsEnabled",
                Scope = StandardCrudRequiredFieldScope.CreateOnly,
                ErrorCode = "Organization.Person.EnabledRequired",
                Message = "人员启用状态不能为空。",
                ReasonKey = "organization.person.enabledRequired"
            },
            new StandardCrudRequiredField
            {
                Field = "EmploymentStatus",
                Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                ErrorCode = "Organization.Person.EmploymentStatusRequired",
                Message = "任职状态不能为空。",
                ReasonKey = "organization.person.employmentStatusRequired"
            }
        ],
        UniqueConstraints =
        [
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["PersonNo"],
                ErrorCode = "Organization.Person.PersonNoDuplicated",
                Message = "人员工号必须唯一。",
                ReasonKey = "organization.person.personNoDuplicated"
            }
        ]
    };

    // Company lookup 只消费统一编译计划，Department 与 Position 暂时保留既有非 canonical 手写计划。
    private static IReadOnlyList<PlatformDisplayValueField> BuildDisplayValueFields()
    {
        var models = OrganizationModelDefinitions.All;
        var person = models.Single(model => string.Equals(model.Name, "Person", StringComparison.Ordinal));
        IModelDisplayProjectionPlanner planner = new ModelDisplayProjectionPlanner();
        var modelDisplayValueFields = new ModelDisplayValueFieldProjector().Project(planner.Compile(person, models));

        return
        [
            .. modelDisplayValueFields,
            new PlatformDisplayValueField("DepartmentName", "DepartmentId", "Department", "EID", "ENAME"),
            new PlatformDisplayValueField("PositionName", "PositionId", "Position", "EID", "ENAME")
        ];
    }
}
