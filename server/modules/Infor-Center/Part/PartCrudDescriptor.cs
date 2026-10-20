using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modules.Part;

/// <summary>HTTP 保持创建与读取；Workspace 通过稳定对象行为调用更新和删除。</summary>
public static class PartCrudDescriptor
{
    /// <summary>从正式 Model 投影字段规则，编码唯一不依赖页面校验。</summary>
    public static StandardCrudDescriptor Create()
    {
        var metadata = new PartModelMetadataProvider();
        return Create(metadata.GetDocument(), metadata.GetDefinitions().Single());
    }

    /// <summary>运行时消费本候选已经验证的 authoring 与模型，不读取工作树或发行目录。</summary>
    public static StandardCrudDescriptor Create(ModelMetadataDocument document, ModelDefinition model)
    {
        string[] fields = ["EID", "ECODE", "ENAME", "Description", "OwnerId", "OwnerDepartmentId", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
        var fieldSet = new StandardCrudFieldSet(fields, fields, ["ECODE", "ENAME", "Description"], ["ECODE", "ENAME", "Description"]);
        return new()
        {
            DataExecutions = new Dictionary<string, string> { ["list"] = "part.part.list", ["read"] = "part.part.read", ["create"] = "part.part.create", ["update"] = "part.part.update", ["delete"] = "part.part.delete" },
            RequiresCreationRecord = true, RequiresUpdateRecord = true,
            ResourceKey = "part.part", ModuleId = "Part", ModelName = "Part", Route = "api/part/parts",
            IdField = "EID", DisplayField = "ENAME", FieldSet = fieldSet, DeletePolicy = StandardCrudDeletePolicy.Hard,
            Authorization = StandardCrudAuthorization.ByFeature("part.part", StandardCrudOperation.List, StandardCrudOperation.Detail, StandardCrudOperation.Create),
            OptionLabelFields = ["ECODE", "ENAME"], SortableFields = ["ECODE", "ENAME", "UpdatedAt"], DefaultSortField = "ECODE",
            FieldPolicies = new ModelStandardCrudFieldPolicyProjector().Project(document, model, fieldSet),
            RequiredFields = [new() { Field = "ECODE", Scope = StandardCrudRequiredFieldScope.CreateAndUpdate, ErrorCode = "Part.CodeRequired", Message = "编码不能为空。", ReasonKey = "Part.CodeRequired" },
                new() { Field = "ENAME", Scope = StandardCrudRequiredFieldScope.CreateAndUpdate, ErrorCode = "Part.NameRequired", Message = "名称不能为空。", ReasonKey = "Part.NameRequired" }],
        };
    }
}
