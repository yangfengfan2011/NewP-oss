using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudDescriptorTests
{
    [Fact]
    public void Validate_WhenDetailFieldsMissId_ThrowsStableMessage()
    {
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["ENAME"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            DeletePolicy = StandardCrudDeletePolicy.None
        };

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的 DetailFields 必须包含 IdField。", ex.Message);
    }

    [Fact]
    public void Validate_WhenUniqueConstraintFieldIsMissingFromDetailFields_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithUniqueConstraints(
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["PersonNo"],
                ErrorCode = "personNoConflict",
                Message = "人员编号已存在。",
                ReasonKey = "personNo"
            });

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的唯一约束 personNo 字段 PersonNo 必须包含在 DetailFields 中。", ex.Message);
    }

    [Fact]
    public void Validate_WhenUniqueConstraintKeyDuplicated_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithUniqueConstraints(
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["ENAME"],
                ErrorCode = "personNoConflict",
                Message = "人员编号已存在。",
                ReasonKey = "personNo"
            },
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["ENAME"],
                ErrorCode = "personNameConflict",
                Message = "人员姓名已存在。",
                ReasonKey = "personName"
            });

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的唯一约束 key personNo 重复。", ex.Message);
    }

    [Fact]
    public void Validate_WhenUniqueConstraintFieldIsBlank_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithUniqueConstraints(
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = [" "],
                ErrorCode = "personNoConflict",
                Message = "人员编号已存在。",
                ReasonKey = "personNo"
            });

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的唯一约束 personNo 字段不能为空。", ex.Message);
    }

    [Fact]
    public void Validate_WhenUniqueConstraintFieldDuplicated_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithUniqueConstraints(
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["ENAME", "ENAME"],
                ErrorCode = "personNoConflict",
                Message = "人员编号已存在。",
                ReasonKey = "personNo"
            });

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的唯一约束 personNo Fields 字段 ENAME 重复。", ex.Message);
    }

    [Fact]
    public void Validate_WhenUniqueConstraintScopeFieldDuplicated_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithUniqueConstraints(
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["ENAME"],
                ScopeFields = ["CompanyId", "CompanyId"],
                ErrorCode = "personNoConflict",
                Message = "人员编号已存在。",
                ReasonKey = "personNo"
            });

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的唯一约束 personNo ScopeFields 字段 CompanyId 重复。", ex.Message);
    }

    [Fact]
    public void Descriptor_CanDeclareRequiredFields()
    {
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "PersonNo", "ENAME"],
                DetailFields: ["EID", "PersonNo", "ENAME"],
                CreateFields: ["PersonNo", "ENAME"],
                UpdateFields: ["ENAME"]),
            RequiredFields =
            [
                new StandardCrudRequiredField
                {
                    Field = "PersonNo",
                    Scope = StandardCrudRequiredFieldScope.CreateOnly,
                    ErrorCode = "Organization.Person.PersonNoRequired",
                    Message = "人员工号不能为空。",
                    ReasonKey = "organization.person.personNoRequired"
                }
            ]
        };

        var required = Assert.Single(descriptor.RequiredFields);

        Assert.Equal("PersonNo", required.Field);
        Assert.Equal(StandardCrudRequiredFieldScope.CreateOnly, required.Scope);
    }

    [Fact]
    public void Validate_WhenConcurrencyRevisionIsNotReadable_ThrowsStableMessage()
    {
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            ConcurrencyRevisionField = "Revision",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"])
        };

        var error = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的并发版本字段 Revision 必须包含在 DetailFields 中。", error.Message);
    }

    [Fact]
    public void Validate_WhenConcurrencyRevisionIsBusinessWritable_ThrowsStableMessage()
    {
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            ConcurrencyRevisionField = "Revision",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "Revision"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME", "Revision"])
        };

        var error = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的并发版本字段 Revision 不得作为业务更新字段声明。", error.Message);
    }

    [Fact]
    public void Validate_WhenRequiredFieldContractMissing_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithRequiredFields(
            new StandardCrudRequiredField
            {
                Field = "ENAME",
                ErrorCode = "",
                Message = "姓名不能为空。",
                ReasonKey = "organization.person.nameRequired"
            });

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 ENAME 缺少 ErrorCode。", ex.Message);
    }

    [Fact]
    public void Validate_WhenRequiredFieldDuplicatedInSameScope_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithRequiredFields(
            Required("ENAME", StandardCrudRequiredFieldScope.CreateAndUpdate),
            Required("ENAME", StandardCrudRequiredFieldScope.CreateAndUpdate));

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 ENAME 在 CreateAndUpdate 范围内重复声明。", ex.Message);
    }

    [Fact]
    public void Validate_WhenRequiredFieldIsNotDeclaredInAnyFieldSet_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithRequiredFields(Required("UnknownField", StandardCrudRequiredFieldScope.CreateOnly));

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 UnknownField 未在字段集合中声明。", ex.Message);
    }

    [Fact]
    public void Validate_WhenRequiredFieldIsNotInDetailFields_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithRequiredFields(Required("PersonNo", StandardCrudRequiredFieldScope.CreateOnly));

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 PersonNo 必须包含在 DetailFields 中。", ex.Message);
    }

    [Fact]
    public void Validate_WhenRequiredCreateFieldIsNotWritable_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithRequiredFields(Required("PositionId", StandardCrudRequiredFieldScope.CreateOnly));

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 PositionId 必须包含在 CreateFields 中。", ex.Message);
    }

    [Fact]
    public void Validate_WhenRequiredUpdateFieldIsNotWritable_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithRequiredFields(
            [Required("PersonNo", StandardCrudRequiredFieldScope.UpdateOnly)],
            includePersonNoInDetail: true);

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 PersonNo 必须包含在 UpdateFields 中。", ex.Message);
    }

    [Fact]
    public void Validate_WhenRequiredErrorOverrideContradictsCanonicalPolicy_ThrowsStableMessage()
    {
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies =
            [
                new StandardCrudFieldPolicy
                {
                    Field = "ENAME",
                    StorageRequired = true,
                    CreateInputRequired = true,
                    UpdateNonEmpty = true,
                    ValueOwner = StandardCrudFieldValueOwner.Caller
                }
            ],
            RequiredFields = [Required("ENAME", StandardCrudRequiredFieldScope.CreateOnly)]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的必填错误覆盖 ENAME 范围 CreateOnly 与规范字段策略 CreateAndUpdate 不一致。",
            ex.Message);
    }

    [Fact]
    public void Adapter_MapsListFieldsAndDisplayValueFieldsToPlatformQueryDescriptor()
    {
        var descriptor = DescriptorWithDisplayValueFields();

        var queryDescriptor = StandardCrudQueryDescriptorAdapter.ToQueryDescriptor(descriptor);

        Assert.Equal(["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled"], queryDescriptor.AllowedModelFields);
        Assert.Equal(["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled", "CompanyName"], queryDescriptor.DefaultFields);
        Assert.Equal(["PersonNo", "ENAME"], queryDescriptor.SortableFields);
        Assert.Equal("PersonNo", queryDescriptor.DefaultSortField);
        Assert.Equal("desc", queryDescriptor.DefaultSortDirection);
        Assert.Equal(["ENAME"], queryDescriptor.StableSortFields);
        var display = Assert.Single(queryDescriptor.DisplayValueFields);
        Assert.Equal("CompanyName", display.Field);
        Assert.Equal("CompanyId", display.SourceField);
    }

    [Fact]
    public void Validate_WhenSortableFieldIsNotAListField_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithSort(["SecretField"], defaultSortField: null);

        var error = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的排序字段 SecretField 未纳入列表字段白名单。",
            error.Message);
    }

    [Fact]
    public void Validate_WhenDefaultSortIsNotSortable_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithSort(["PersonNo"], defaultSortField: "ENAME");

        var error = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的默认排序字段 ENAME 未纳入排序白名单。",
            error.Message);
    }

    [Fact]
    public void Validate_WhenStableSortFieldIsNotAListField_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithSort(["PersonNo"], defaultSortField: "PersonNo", stableSortFields: ["SecretField"]);

        var error = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的稳定排序字段 SecretField 未纳入列表字段白名单。",
            error.Message);
    }

    [Fact]
    public void Validate_WhenDisplayValueSourceFieldIsNotListField_ThrowsStableMessage()
    {
        var descriptor = DescriptorWithDisplayValueFields(sourceField: "SecretCompanyId");

        var error = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的 display value 字段 CompanyName 引用了未纳入列表字段白名单的 source 字段 SecretCompanyId。",
            error.Message);
    }

    private static StandardCrudRequiredField Required(
        string field,
        StandardCrudRequiredFieldScope scope = StandardCrudRequiredFieldScope.CreateAndUpdate) => new()
    {
        Field = field,
        Scope = scope,
        ErrorCode = $"Organization.Person.{field}Required",
        Message = $"{field} 不能为空。",
        ReasonKey = $"organization.person.{field}Required"
    };

    private static StandardCrudDescriptor DescriptorWithRequiredFields(
        params StandardCrudRequiredField[] requiredFields) =>
        DescriptorWithRequiredFields(requiredFields, includePersonNoInDetail: false);

    private static StandardCrudDescriptor DescriptorWithRequiredFields(
        IReadOnlyList<StandardCrudRequiredField> requiredFields,
        bool includePersonNoInDetail)
    {
        return new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME", "PersonNo", "PositionId"],
                DetailFields: includePersonNoInDetail
                    ? ["EID", "ENAME", "PersonNo", "PositionId"]
                    : ["EID", "ENAME", "PositionId"],
                CreateFields: ["ENAME", "PersonNo"],
                UpdateFields: ["ENAME"]),
            RequiredFields = requiredFields
        };
    }

    private static StandardCrudDescriptor DescriptorWithUniqueConstraints(params StandardCrudUniqueConstraint[] constraints)
    {
        return new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME", "PersonNo", "CompanyId"],
                DetailFields: ["EID", "ENAME", "CompanyId"],
                CreateFields: ["ENAME", "PersonNo", "CompanyId"],
                UpdateFields: ["ENAME", "PersonNo", "CompanyId"]),
            DeletePolicy = StandardCrudDeletePolicy.None,
            UniqueConstraints = constraints
        };
    }

    private static StandardCrudDescriptor DescriptorWithDisplayValueFields(string sourceField = "CompanyId") => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "IsEnabled"]),
        SortableFields = ["PersonNo", "ENAME"],
        DefaultSortField = "PersonNo",
        DefaultSortDirection = "desc",
        StableSortFields = ["ENAME"],
        DisplayValueFields =
        [
            new PlatformDisplayValueField("CompanyName", sourceField, "Company", "EID", "ENAME")
        ]
    };

    private static StandardCrudDescriptor DescriptorWithSort(
        IReadOnlyList<string> sortableFields,
        string? defaultSortField,
        IReadOnlyList<string>? stableSortFields = null) => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME"],
            DetailFields: ["EID", "PersonNo", "ENAME"],
            CreateFields: ["PersonNo", "ENAME"],
            UpdateFields: ["PersonNo", "ENAME"]),
        SortableFields = sortableFields,
        DefaultSortField = defaultSortField,
        StableSortFields = stableSortFields ?? []
    };
}
