using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modules.Example.Persistence.Descriptors;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Modules.Example.Crud;

public static class ExampleRecordCrudDescriptorFactory
{
    /// <summary>
    /// 创建示例记录的标准 CRUD 描述符，模块只声明字段、授权和删除边界。
    /// </summary>
    public static StandardCrudDescriptor Create()
    {
        var fieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ECODE", "ENAME", "ReviewerUserEID", "RecordSource"],
            DetailFields: ["EID", "ECODE", "ENAME", "ReviewerUserEID", "RecordSource", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
            CreateFields: ["ECODE", "ENAME", "ReviewerUserEID", "RecordSource"],
            UpdateFields: ["ENAME", "ReviewerUserEID", "RecordSource"]);
        return new StandardCrudDescriptor
        {
            ResourceKey = "example.record",
            DataExecutions = new Dictionary<string, string>
            {
                ["read"] = "example.record.read", ["list"] = "example.record.list",
                ["export"] = "example.record.export"
            },
            ModuleId = "Example",
            ModelName = "ExampleRecord",
            Route = "api/example/records",
            IdField = "EID",
            DisplayField = "ENAME",
            SortableFields = ["ECODE", "ENAME"],
            DefaultSortField = "ECODE",
            DefaultSortDirection = "asc",
            Authorization = StandardCrudAuthorization.ByFeature("example.record",
                StandardCrudOperation.List, StandardCrudOperation.Detail, StandardCrudOperation.Create,
                StandardCrudOperation.Update, StandardCrudOperation.Delete, StandardCrudOperation.Export),
            FieldSet = fieldSet,
            OptionLabelFields = ["ECODE", "ENAME"],
            DeletePolicy = StandardCrudDeletePolicy.Hard,
            FieldPolicies = BuildFieldPolicies(fieldSet),
            RequiredFields =
            [
                new StandardCrudRequiredField
                {
                    Field = "ECODE",
                    Scope = StandardCrudRequiredFieldScope.CreateOnly,
                    ErrorCode = "Example.Record.CodeRequired",
                    Message = "示例记录代码不能为空。",
                    ReasonKey = "example.record.codeRequired"
                },
                new StandardCrudRequiredField
                {
                    Field = "ENAME",
                    Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                    ErrorCode = "Example.Record.NameRequired",
                    Message = "示例记录名称不能为空。",
                    ReasonKey = "example.record.nameRequired"
                },
                new StandardCrudRequiredField
                {
                    Field = "RecordSource",
                    Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                    ErrorCode = "Example.Record.SourceRequired",
                    Message = "示例记录来源不能为空。",
                    ReasonKey = "example.record.sourceRequired"
                }
            ],
            UniqueConstraints =
            [
                new StandardCrudUniqueConstraint
                {
                    Key = "code",
                    Fields = ["ECODE"],
                    ErrorCode = "Example.Record.CodeDuplicated",
                    Message = "示例记录代码必须唯一。",
                    ReasonKey = "example.record.codeDuplicated"
                }
            ]
        };
    }

    // Required 的存储、输入、所有权和默认值只从 Example 的真实 Model authoring 投影。
    private static IReadOnlyList<StandardCrudFieldPolicy> BuildFieldPolicies(StandardCrudFieldSet fieldSet)
    {
        var provider = new ExampleModelMetadataProvider();
        var document = provider.GetDocument();
        var model = provider.GetDefinitions().Single();
        return new ModelStandardCrudFieldPolicyProjector().Project(document, model, fieldSet);
    }

}
