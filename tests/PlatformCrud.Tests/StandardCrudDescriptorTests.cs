using ICP.Platform.Crud.Descriptors;
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
}
