using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public static class DepartmentCrudDescriptorFactory
{
    /// <summary>
    /// 创建部门层级目录的列表、普通编辑与条件删除描述符；公司与编码在创建后不可修改。
    /// </summary>
    public static StandardCrudDescriptor Create()
    {
        var fieldSet = new StandardCrudFieldSet(
            ListFields:
            [
                "EID", "CompanyId", "ParentDepartmentId", "ECODE", "ENAME", "IsEnabled", "SortOrder", "UpdatedAt"
            ],
            DetailFields:
            [
                "EID", "CompanyId", "ParentDepartmentId", "ECODE", "ENAME", "IsEnabled", "SortOrder",
                "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"
            ],
            CreateFields: ["CompanyId", "ParentDepartmentId", "ECODE", "ENAME", "SortOrder"],
            UpdateFields: ["ParentDepartmentId", "ENAME", "SortOrder", "IsEnabled"]);
        return new StandardCrudDescriptor
        {
            DataExecutions = new Dictionary<string, string> { ["create"] = "organization.department.create", ["update"] = "organization.department.update", ["delete"] = "organization.department.delete", ["list"] = "organization.department.list", ["read"] = "organization.department.read" },
            ResourceKey = "organization.department",
            ModuleId = "Organization",
            ModelName = "Department",
            Route = "api/organization/departments",
            IdField = "EID",
            DisplayField = "ENAME",
            EnabledField = "IsEnabled",
            SortableFields = ["CompanyId", "ECODE", "ENAME", "IsEnabled", "SortOrder", "UpdatedAt"],
            DefaultSortField = "SortOrder",
            DefaultSortDirection = "asc",
            StableSortFields = ["ECODE"],
            Authorization = StandardCrudAuthorization.ByFeature("organization.department", StandardCrudOperation.List, StandardCrudOperation.Detail, StandardCrudOperation.Create, StandardCrudOperation.Update, StandardCrudOperation.Delete),
            FieldSet = fieldSet,
            OptionLabelFields = ["ECODE", "ENAME"],
            DeletePolicy = StandardCrudDeletePolicy.Conditional,
            FieldPolicies = BuildFieldPolicies(fieldSet),
            RequiredFields =
            [
                Required("CompanyId", "Company", StandardCrudRequiredFieldScope.CreateOnly),
                Required("ECODE", "Code", StandardCrudRequiredFieldScope.CreateOnly),
                Required("ENAME", "Name", StandardCrudRequiredFieldScope.CreateAndUpdate),
                Required("SortOrder", "SortOrder", StandardCrudRequiredFieldScope.CreateAndUpdate)
            ],
            UniqueConstraints =
            [
                Unique("code", ["ECODE"], ["CompanyId"], "Code", "部门编码在所属公司内必须唯一。"),
                Unique(
                    "siblingName",
                    ["ENAME"],
                    ["CompanyId", "ParentDepartmentId"],
                    "Name",
                    "部门名称在所属公司同一上级下必须唯一。")
            ]
        };
    }

    // Department Model 是字段、显示规则与表单归属真源；本票只覆盖两个服务端创建事实。
    private static IReadOnlyList<StandardCrudFieldPolicy> BuildFieldPolicies(StandardCrudFieldSet fieldSet)
    {
        var provider = new OrganizationModelMetadataProvider();
        var document = provider.GetDocument("Department");
        var model = provider.GetDefinitions().Single(definition => definition.Name == "Department");
        return new ModelStandardCrudFieldPolicyProjector()
            .Project(document, model, fieldSet)
            .Select(policy => policy.Field switch
            {
                "CompanyId" or "ECODE" => CopyPolicy(
                    policy,
                    StandardCrudFieldValueOwner.CallerOnCreate),
                "IsEnabled" => CopyPolicy(
                    policy,
                    StandardCrudFieldValueOwner.ServerOnCreate,
                    createDefaultValue: true),
                _ => policy
            })
            .ToArray();
    }

    // 覆盖字段 owner 时保留 Model 投影的类型、必填和短文本规则。
    private static StandardCrudFieldPolicy CopyPolicy(
        StandardCrudFieldPolicy policy,
        StandardCrudFieldValueOwner owner,
        object? createDefaultValue = null) => new()
    {
        Field = policy.Field,
        ValueType = policy.ValueType,
        StorageRequired = policy.StorageRequired,
        CreateInputRequired = policy.CreateInputRequired,
        UpdateNonEmpty = policy.UpdateNonEmpty,
        ValueOwner = owner,
        CreateDefaultValue = createDefaultValue,
        Trim = policy.Trim,
        MaxLength = policy.MaxLength,
        EmptyAsNull = policy.EmptyAsNull
    };

    // 必填声明使用资源稳定码，使共享表单能把错误精确绑定到字段。
    private static StandardCrudRequiredField Required(
        string field,
        string codeStem,
        StandardCrudRequiredFieldScope scope) => new()
    {
        Field = field,
        Scope = scope,
        ErrorCode = $"Organization.Department.{codeStem}Required",
        Message = $"字段 {field} 不能为空。",
        ReasonKey = $"organization.department.{char.ToLowerInvariant(codeStem[0])}{codeStem[1..]}Required"
    };

    // 部门编码以 CompanyId 为作用域，名称以 CompanyId 与 ParentDepartmentId 为同级作用域。
    private static StandardCrudUniqueConstraint Unique(
        string key,
        IReadOnlyList<string> fields,
        IReadOnlyList<string> scopeFields,
        string codeStem,
        string message) => new()
    {
        Key = key,
        Fields = fields,
        ScopeFields = scopeFields,
        CaseInsensitive = true,
        TrimWhitespace = true,
        ErrorCode = $"Organization.Department.{codeStem}Duplicated",
        Message = message,
        ReasonKey = $"organization.department.{key}Duplicated"
    };
}
