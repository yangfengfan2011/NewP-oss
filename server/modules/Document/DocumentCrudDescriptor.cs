using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;

namespace ICP.Platform.Modules.Document;

/// <summary>只开放最小创建与读取；后续 Workspace 消费同一稳定资源身份。</summary>
public static class DocumentCrudDescriptor
{
    /// <summary>从正式 Model 投影字段规则，编码唯一不依赖页面校验。</summary>
    public static StandardCrudDescriptor Create()
    {
        string[] fields = ["EID", "ECODE", "ENAME", "Description", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
        var fieldSet = new StandardCrudFieldSet(fields, fields, ["ECODE", "ENAME", "Description"], []);
        var metadata = new DocumentModelMetadataProvider();
        return new()
        {
            ResourceKey = "document.document", ModuleId = "Document", ModelName = "Document", Route = "api/document/documents",
            IdField = "EID", DisplayField = "ENAME", FieldSet = fieldSet,
            Authorization = StandardCrudAuthorization.ByFeature("document.document", StandardCrudOperation.List, StandardCrudOperation.Detail, StandardCrudOperation.Create),
            OptionLabelFields = ["ECODE", "ENAME"], SortableFields = ["ECODE", "ENAME", "UpdatedAt"], DefaultSortField = "ECODE",
            FieldPolicies = new ModelStandardCrudFieldPolicyProjector().Project(metadata.GetDocument(), metadata.GetDefinitions().Single(), fieldSet),
            RequiredFields = [new() { Field = "ECODE", Scope = StandardCrudRequiredFieldScope.CreateOnly, ErrorCode = "Document.CodeRequired", Message = "编码不能为空。", ReasonKey = "Document.CodeRequired" },
                new() { Field = "ENAME", Scope = StandardCrudRequiredFieldScope.CreateOnly, ErrorCode = "Document.NameRequired", Message = "名称不能为空。", ReasonKey = "Document.NameRequired" }],
        };
    }
}
