using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public static class PositionCrudDescriptorFactory
{
    /// <summary>
    /// 创建全局岗位的列表、写入与条件删除描述符；创建启用默认值由服务端拥有，删除由模块原子执行器裁决。
    /// </summary>
    public static StandardCrudDescriptor Create()
    {
        var fieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ENAME", "Description", "IsEnabled"],
            DetailFields: ["EID", "ENAME", "Description", "IsEnabled", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
            CreateFields: ["ENAME", "Description"],
            UpdateFields: ["ENAME", "Description", "IsEnabled"]);
        return new StandardCrudDescriptor
        {
            LegacyDataExecutions = ["create", "update", "delete"],
            ResourceKey = "organization.position",
            ModuleId = "Organization",
            ModelName = "Position",
            Route = "api/organization/positions",
            IdField = "EID",
            DisplayField = "ENAME",
            EnabledField = "IsEnabled",
            SortableFields = ["ENAME", "IsEnabled"],
            DefaultSortField = "ENAME",
            DefaultSortDirection = "asc",
            Authorization = StandardCrudAuthorization.ByFeature("organization.position", StandardCrudOperation.List, StandardCrudOperation.Detail, StandardCrudOperation.Create, StandardCrudOperation.Update, StandardCrudOperation.Delete, StandardCrudOperation.Options),
            FieldSet = fieldSet,
            OptionLabelFields = ["ENAME"],
            DeletePolicy = StandardCrudDeletePolicy.Conditional,
            FieldPolicies = BuildFieldPolicies(fieldSet),
            RequiredFields =
            [
                new StandardCrudRequiredField
                {
                    Field = "ENAME",
                    Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                    ErrorCode = "Organization.Position.NameRequired",
                    Message = "岗位名称不能为空。",
                    ReasonKey = "organization.position.nameRequired"
                }
            ],
            UniqueConstraints =
            [
                new StandardCrudUniqueConstraint
                {
                    Key = "name",
                    Fields = ["ENAME"],
                    ErrorCode = "Organization.Position.NameDuplicated",
                    Message = "岗位名称必须全局唯一。",
                    ReasonKey = "organization.position.nameDuplicated"
                }
            ]
        };
    }

    // Model authoring 投影共享短文本与审计字段策略；IsEnabled 由服务端在创建时固定补 true。
    private static IReadOnlyList<StandardCrudFieldPolicy> BuildFieldPolicies(StandardCrudFieldSet fieldSet)
    {
        var provider = new OrganizationModelMetadataProvider();
        var document = provider.GetDocument("Position");
        var model = provider.GetDefinitions().Single(definition => definition.Name == "Position");
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
}
