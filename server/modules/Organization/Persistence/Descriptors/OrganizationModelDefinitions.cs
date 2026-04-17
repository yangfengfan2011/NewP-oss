using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Organization.Persistence.Descriptors;

public static class OrganizationModelDefinitions
{
    public static readonly ModelDefinition Company = new(
        "Company",
        "Company",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("SortOrder", "SortOrder", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_Company_Code", new[] { "Code" }, true)
        });

    public static readonly ModelDefinition Department = new(
        "Department",
        "Department",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("CompanyId", "CompanyId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("ParentDepartmentId", "ParentDepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("SortOrder", "SortOrder", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        },
        new[]
        {
            new IndexDefinition("IX_Department_Company", new[] { "CompanyId" }, false),
            new IndexDefinition("IX_Department_Parent", new[] { "ParentDepartmentId" }, false),
            new IndexDefinition("IX_Department_Company_Parent_Code", new[] { "CompanyId", "ParentDepartmentId", "Code" }, false)
        });

    public static readonly ModelDefinition Position = new(
        "Position",
        "Position",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("CompanyId", "CompanyId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("DepartmentId", "DepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("EmploymentStatus", "EmploymentStatus", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false),
            new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("SortOrder", "SortOrder", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        },
        new[]
        {
            new IndexDefinition("IX_Position_Department", new[] { "DepartmentId" }, false),
            new IndexDefinition("IX_Position_Company", new[] { "CompanyId" }, false)
        });

    public static readonly ModelDefinition Person = new(
        "Person",
        "Person",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("PersonNo", "PersonNo", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CompanyId", "CompanyId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("DepartmentId", "DepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("PositionId", "PositionId", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("EmploymentStatus", "EmploymentStatus", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false),
            new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        },
        new[]
        {
            new IndexDefinition("IX_Person_Company", new[] { "CompanyId" }, false),
            new IndexDefinition("IX_Person_Department", new[] { "DepartmentId" }, false),
            new IndexDefinition("UX_Person_Company_PersonNo", new[] { "CompanyId", "PersonNo" }, true)
        });

    public static readonly ModelDefinition PersonUserLink = new(
        "PersonUserLink",
        "PersonUserLink",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("PersonId", "PersonId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("UserId", "UserId", FieldKind.Reference, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("LinkedAt", "LinkedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("LinkedBy", "LinkedBy", FieldKind.Reference, FieldValueType.Text, "string", true, false, false)
        },
        new[]
        {
            new IndexDefinition("UX_PersonUserLink_Person", new[] { "PersonId" }, true),
            new IndexDefinition("UX_PersonUserLink_User", new[] { "UserId" }, true)
        });

    public static readonly IReadOnlyList<ModelDefinition> All = new[]
    {
        Company,
        Department,
        Position,
        Person,
        PersonUserLink
    };
}
