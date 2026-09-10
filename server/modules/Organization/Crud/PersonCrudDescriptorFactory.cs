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
    public static StandardCrudDescriptor Create()
    {
        var fieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "BirthDate", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt", "IsEnabled", "AccountEID"],
            DetailFields: ["EID", "PersonNo", "ENAME", "BirthDate", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt", "IsEnabled", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt", "AccountEID", "AccountBoundAt", "AccountBoundBy", "AccountBindingReason"],
            CreateFields: ["PersonNo", "ENAME", "BirthDate", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt", "IsEnabled"],
            UpdateFields: ["ENAME", "BirthDate", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt", "UpdatedAt"]);
        return new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            RequiresCreationRecord = true,
            RequiresUpdateRecord = true,
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            SortableFields = ["PersonNo", "ENAME", "BirthDate", "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt", "IsEnabled"],
            DefaultSortField = "PersonNo",
            DefaultSortDirection = "asc",
            Authorization = StandardCrudAuthorization.ByConvention(OrganizationPermissions.PersonStem),
            EnabledField = "IsEnabled",
            FieldSet = fieldSet,
            OptionLabelFields = ["PersonNo", "ENAME"],
            SummaryBlockKeys = ["userBinding", "authorizationSummary"],
            DisplayValueFields = BuildDisplayValueFields(),
            FieldPolicies = BuildFieldPolicies(fieldSet),
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
    }

    // 这里只保留既有非 canonical 业务 companion；规范 Model 计划由平台资源解析装配。
    private static IReadOnlyList<PlatformDisplayValueField> BuildDisplayValueFields()
    {

        return
        [
            new PlatformDisplayValueField("DepartmentName", "DepartmentId", "Department", "EID", "ENAME"),
            new PlatformDisplayValueField("PositionName", "PositionId", "Position", "EID", "ENAME")
        ];
    }

    // Person 的标准写入策略来自真实 Model；IsEnabled 保持创建必填契约。
    private static IReadOnlyList<StandardCrudFieldPolicy> BuildFieldPolicies(StandardCrudFieldSet fieldSet)
    {
        var provider = new OrganizationModelMetadataProvider();
        var policies = new ModelStandardCrudFieldPolicyProjector()
            .Project(
                provider.GetDocument("Person"),
                provider.GetDefinitions().Single(model => model.Name == "Person"),
                fieldSet);

        return policies.Select(policy => policy.Field switch
            {
                "IsEnabled" => new StandardCrudFieldPolicy
                {
                    Field = policy.Field,
                    ValueType = policy.ValueType,
                    StorageRequired = policy.StorageRequired,
                    CreateInputRequired = true,
                    ValueOwner = StandardCrudFieldValueOwner.Caller,
                    CreateDefaultValue = policy.CreateDefaultValue
                },
                _ => policy
            })
            .ToArray();
    }
}
