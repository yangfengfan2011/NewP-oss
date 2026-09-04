using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public static class CompanyCrudDescriptorFactory
{
    /// <summary>
    /// 创建公司目录的列表、普通编辑与条件删除描述符；编码只在创建时写入，启用初值由服务端持有。
    /// </summary>
    public static StandardCrudDescriptor Create()
    {
        var fieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ECODE", "ENAME", "IsEnabled", "SortOrder", "UpdatedAt"],
            DetailFields: ["EID", "ECODE", "ENAME", "IsEnabled", "SortOrder", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
            CreateFields: ["ECODE", "ENAME", "SortOrder"],
            UpdateFields: ["ENAME", "SortOrder", "IsEnabled"]);
        return new StandardCrudDescriptor
        {
            ResourceKey = "organization.company",
            ModuleId = "Organization",
            ModelName = "Company",
            Route = "api/organization/companies",
            IdField = "EID",
            DisplayField = "ENAME",
            EnabledField = "IsEnabled",
            SortableFields = ["ECODE", "ENAME", "IsEnabled", "SortOrder", "UpdatedAt"],
            DefaultSortField = "SortOrder",
            DefaultSortDirection = "asc",
            StableSortFields = ["ECODE"],
            Authorization = StandardCrudAuthorization.ByConvention(OrganizationPermissions.CompanyStem),
            FieldSet = fieldSet,
            OptionLabelFields = ["ECODE", "ENAME"],
            DeletePolicy = StandardCrudDeletePolicy.Conditional,
            FieldPolicies = BuildFieldPolicies(fieldSet),
            RequiredFields =
            [
                Required("ECODE", "Code", StandardCrudRequiredFieldScope.CreateOnly),
                Required("ENAME", "Name", StandardCrudRequiredFieldScope.CreateAndUpdate),
                Required("SortOrder", "SortOrder", StandardCrudRequiredFieldScope.CreateAndUpdate)
            ],
            UniqueConstraints =
            [
                Unique("code", "ECODE", "Code", "公司编码必须全局唯一。"),
                Unique("name", "ENAME", "Name", "公司名称必须全局唯一。")
            ]
        };
    }

    // Company Model 是字段形状与表单归属真源；这里只覆盖 IsEnabled 创建初值的服务端 owner。
    private static IReadOnlyList<StandardCrudFieldPolicy> BuildFieldPolicies(StandardCrudFieldSet fieldSet)
    {
        var provider = new OrganizationModelMetadataProvider();
        var document = provider.GetDocument("Company");
        var model = provider.GetDefinitions().Single(definition => definition.Name == "Company");
        return new ModelStandardCrudFieldPolicyProjector()
            .Project(document, model, fieldSet)
            .Select(policy => policy.Field == "IsEnabled"
                ? new StandardCrudFieldPolicy
                {
                    Field = policy.Field,
                    ValueType = policy.ValueType,
                    StorageRequired = true,
                    ValueOwner = StandardCrudFieldValueOwner.ServerOnCreate,
                    CreateDefaultValue = true
                }
                : policy)
            .ToArray();
    }

    // 必填声明保留资源稳定码，使共享表单能把错误精确绑定到字段。
    private static StandardCrudRequiredField Required(
        string field,
        string codeStem,
        StandardCrudRequiredFieldScope scope) => new()
    {
        Field = field,
        Scope = scope,
        ErrorCode = $"Organization.Company.{codeStem}Required",
        Message = $"字段 {field} 不能为空。",
        ReasonKey = $"organization.company.{char.ToLowerInvariant(codeStem[0])}{codeStem[1..]}Required"
    };

    // 公司编码与名称都使用相同的 trim 后、不区分大小写的全局唯一比较。
    private static StandardCrudUniqueConstraint Unique(
        string key,
        string field,
        string codeStem,
        string message) => new()
        {
            Key = key,
            Fields = [field],
            CaseInsensitive = true,
            TrimWhitespace = true,
            ErrorCode = $"Organization.Company.{codeStem}Duplicated",
            Message = message,
            ReasonKey = $"organization.company.{key}Duplicated"
        };
}
