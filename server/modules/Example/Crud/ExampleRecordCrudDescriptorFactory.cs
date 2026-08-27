using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Modules.Example.Crud;

public static class ExampleRecordCrudDescriptorFactory
{
    /// <summary>
    /// 创建示例记录的标准 CRUD 描述符，模块只声明字段、授权和删除边界。
    /// </summary>
    public static StandardCrudDescriptor Create() => new()
    {
        ResourceKey = "example.record",
        ModuleId = "Example",
        ModelName = "ExampleRecord",
        Route = "api/example/records",
        IdField = "EID",
        DisplayField = "ENAME",
        SortableFields = ["ECODE", "ENAME"],
        DefaultSortField = "ECODE",
        DefaultSortDirection = "asc",
        Authorization = StandardCrudAuthorization.ByConvention(ExamplePermissions.RecordStem),
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ECODE", "ENAME"],
            DetailFields: ["EID", "ECODE", "ENAME", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
            CreateFields: ["ECODE", "ENAME"],
            UpdateFields: ["ENAME"]),
        OptionLabelFields = ["ECODE", "ENAME"],
        DeletePolicy = StandardCrudDeletePolicy.Hard,
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
