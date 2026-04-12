using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Organization.Persistence.Descriptors;

public static class OrganizationModelDefinitions
{
    public static readonly ModelDefinition Company = new(
        "Company",
        "org_companies",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Code", "code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("IsEnabled", "is_enabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("SortOrder", "sort_order", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CreatedAt", "created_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "updated_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        },
        new[]
        {
            new IndexDefinition("UX_Company_Code", new[] { "Code" }, true)
        });

    public static readonly ModelDefinition Department = new(
        "Department",
        "org_departments",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("CompanyId", "company_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("ParentDepartmentId", "parent_department_id", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("Code", "code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("IsEnabled", "is_enabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("SortOrder", "sort_order", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CreatedAt", "created_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "updated_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        },
        new[]
        {
            new IndexDefinition("IX_Department_Company", new[] { "CompanyId" }, false),
            new IndexDefinition("IX_Department_Parent", new[] { "ParentDepartmentId" }, false),
            new IndexDefinition("IX_Department_Company_Parent_Code", new[] { "CompanyId", "ParentDepartmentId", "Code" }, false)
        });

    public static readonly ModelDefinition Position = new(
        "Position",
        "org_positions",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("CompanyId", "company_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("DepartmentId", "department_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("Code", "code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("EmploymentStatus", "employment_status", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false),
            new FieldDefinition("IsEnabled", "is_enabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("SortOrder", "sort_order", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CreatedAt", "created_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "updated_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        },
        new[]
        {
            new IndexDefinition("IX_Position_Department", new[] { "DepartmentId" }, false),
            new IndexDefinition("IX_Position_Company", new[] { "CompanyId" }, false)
        });

    public static readonly ModelDefinition Person = new(
        "Person",
        "org_persons",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("PersonNo", "person_no", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CompanyId", "company_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("DepartmentId", "department_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("PositionId", "position_id", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("EmploymentStatus", "employment_status", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false),
            new FieldDefinition("IsEnabled", "is_enabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("CreatedAt", "created_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "updated_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        },
        new[]
        {
            new IndexDefinition("IX_Person_Company", new[] { "CompanyId" }, false),
            new IndexDefinition("IX_Person_Department", new[] { "DepartmentId" }, false),
            new IndexDefinition("UX_Person_Company_PersonNo", new[] { "CompanyId", "PersonNo" }, true)
        });

    public static readonly ModelDefinition PersonUserLink = new(
        "PersonUserLink",
        "org_person_user_links",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("PersonId", "person_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("UserId", "user_id", FieldKind.Reference, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("LinkedAt", "linked_at", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("LinkedBy", "linked_by", FieldKind.Reference, FieldValueType.Text, "string", true, false, false)
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
