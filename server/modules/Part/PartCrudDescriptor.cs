using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;

namespace ICP.Platform.Modules.Part;

/// <summary>HTTP 保持创建与读取；Workspace 通过稳定对象行为调用更新和删除。</summary>
public static class PartCrudDescriptor
{
    /// <summary>从正式 Model 投影字段规则，编码唯一不依赖页面校验。</summary>
    public static StandardCrudDescriptor Create()
    {
        string[] fields = ["EID", "ECODE", "ENAME", "Description", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
        var fieldSet = new StandardCrudFieldSet(fields, fields, ["ECODE", "ENAME", "Description"], ["ECODE", "ENAME", "Description"]);
        var metadata = new PartModelMetadataProvider();
        return new()
        {
            ResourceKey = "part.part", ModuleId = "Part", ModelName = "Part", Route = "api/part/parts",
            IdField = "EID", DisplayField = "ENAME", FieldSet = fieldSet, DeletePolicy = StandardCrudDeletePolicy.Hard,
            Authorization = StandardCrudAuthorization.ByFeature("part.part", StandardCrudOperation.List, StandardCrudOperation.Detail, StandardCrudOperation.Create),
            OptionLabelFields = ["ECODE", "ENAME"], SortableFields = ["ECODE", "ENAME", "UpdatedAt"], DefaultSortField = "ECODE",
            FieldPolicies = new ModelStandardCrudFieldPolicyProjector().Project(metadata.GetDocument(), metadata.GetDefinitions().Single(), fieldSet),
            RequiredFields = [new() { Field = "ECODE", Scope = StandardCrudRequiredFieldScope.CreateAndUpdate, ErrorCode = "Part.CodeRequired", Message = "编码不能为空。", ReasonKey = "Part.CodeRequired" },
                new() { Field = "ENAME", Scope = StandardCrudRequiredFieldScope.CreateAndUpdate, ErrorCode = "Part.NameRequired", Message = "名称不能为空。", ReasonKey = "Part.NameRequired" }],
        };
    }
}
