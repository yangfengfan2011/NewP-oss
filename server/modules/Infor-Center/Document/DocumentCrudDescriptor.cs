using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modules.Document;

/// <summary>HTTP 保持创建与读取；Workspace 通过稳定对象行为调用更新和删除。</summary>
public static class DocumentCrudDescriptor
{
    /// <summary>从正式 Model 投影字段规则，编码唯一不依赖页面校验。</summary>
    public static StandardCrudDescriptor Create()
    {
        var metadata = new DocumentModelMetadataProvider();
        return Create(metadata.GetDocument(), metadata.GetDefinitions().Single(model => model.LogicalKey == "Document"));
    }

    /// <summary>运行时消费本候选已经验证的 authoring 与模型，不读取工作树或发行目录。</summary>
    public static StandardCrudDescriptor Create(ModelMetadataDocument document, ModelDefinition model)
    {
        string[] fields = ["EID", "ECODE", "ENAME", "Description", "OwnerId", "OwnerDepartmentId", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
        var fieldSet = new StandardCrudFieldSet(fields, fields, ["ECODE", "ENAME", "Description"], ["ECODE", "ENAME", "Description"]);
        return new()
        {
            DataExecutions = new Dictionary<string, string> { ["list"] = "document.document.list", ["read"] = "document.document.read", ["create"] = "document.document.create", ["update"] = "document.document.update", ["delete"] = "document.document.delete" },
            ResourceKey = "document.document", ModuleId = "Document", ModelName = "Document", Route = "api/document/documents",
            RequiresCreationRecord = true, RequiresUpdateRecord = true,
            SummaryBlockKeys = ["files"],
            IdField = "EID", DisplayField = "ENAME", FieldSet = fieldSet, DeletePolicy = StandardCrudDeletePolicy.Hard,
            Authorization = StandardCrudAuthorization.ByFeature("document.document", StandardCrudOperation.List, StandardCrudOperation.Detail, StandardCrudOperation.Create),
            OptionLabelFields = ["ECODE", "ENAME"], SortableFields = ["ECODE", "ENAME", "UpdatedAt"], DefaultSortField = "ECODE",
            FieldPolicies = new ModelStandardCrudFieldPolicyProjector().Project(document, model, fieldSet),
            RequiredFields = [new() { Field = "ECODE", Scope = StandardCrudRequiredFieldScope.CreateAndUpdate, ErrorCode = "Document.CodeRequired", Message = "编码不能为空。", ReasonKey = "Document.CodeRequired" },
                new() { Field = "ENAME", Scope = StandardCrudRequiredFieldScope.CreateAndUpdate, ErrorCode = "Document.NameRequired", Message = "名称不能为空。", ReasonKey = "Document.NameRequired" }],
        };
    }
}
