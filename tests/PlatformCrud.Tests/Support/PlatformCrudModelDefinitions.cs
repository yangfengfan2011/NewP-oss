using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Crud.Tests.Support;

public static class PlatformCrudModelDefinitions
{
    public static ModelDefinition Person() => new(
        Name: "Person",
        TableName: "Person",
        Kind: ModelKind.Entity,
        Fields:
        [
            new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("PersonNo", "PersonNo", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CompanyId", "CompanyId", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("DepartmentId", "DepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("PositionId", "PositionId", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("EmploymentStatus", "EmploymentStatus", FieldKind.Scalar, FieldValueType.Enum, "string", false, false, false),
            new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
            new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
        ],
        Indexes: []);
}
