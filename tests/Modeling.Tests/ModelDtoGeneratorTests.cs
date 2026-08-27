using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelDtoGeneratorTests
{
    [Fact]
    public void Generate_ForReferenceAndEnumFields_AddsDisplayValueCompanions()
    {
        var definition = new ModelDefinition(
            "Person",
            "Person",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
                new FieldDefinition("DepartmentId", "DepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
                new FieldDefinition("EmploymentStatus", "EmploymentStatus", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false),
                new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
            },
            Array.Empty<IndexDefinition>());

        var code = GenerateCode(definition);

        Assert.Contains("public sealed class PersonDto", code);
        Assert.Contains("public string DepartmentId { get; init; }", code);
        Assert.Contains("public string? DepartmentId_DisplayValue { get; init; }", code);
        Assert.Contains("public string EmploymentStatus { get; init; }", code);
        Assert.Contains("public string? EmploymentStatus_DisplayValue { get; init; }", code);
        Assert.DoesNotContain("Name_DisplayValue", code);
    }

    [Fact]
    public void Generate_ForUserFieldWithDisplayRule_AddsDisplayValueCompanionWithoutAffectingAuditUsers()
    {
        var definition = new ModelDefinition(
            "ExampleRecord",
            "ExampleRecord",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("CreatedBy", "CreatedBy", FieldKind.Audit, FieldValueType.User, "string", true, false, true),
                new FieldDefinition(
                    "ReviewerUserEID",
                    "ReviewerUserEID",
                    FieldKind.Scalar,
                    FieldValueType.User,
                    "string",
                    false,
                    false,
                    false,
                    new FieldDisplayRuleDefinition("user", "IdentityAccount", "EID", "ENAME"))
            },
            Array.Empty<IndexDefinition>());

        var code = GenerateCode(definition);

        Assert.Contains("public string? ReviewerUserEID { get; init; }", code);
        Assert.Contains("public string? ReviewerUserEID_DisplayValue { get; init; }", code);
        Assert.DoesNotContain("CreatedBy_DisplayValue", code);
    }

    [Fact]
    public void Generate_UsesStableAliasesForFixedFields()
    {
        var definition = new ModelDefinition(
            "Person",
            "Person",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
                new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
                new FieldDefinition("ECODE", "ECODE", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
            },
            Array.Empty<IndexDefinition>());

        var code = GenerateCode(definition);

        Assert.Contains("public string Id { get; init; }", code);
        Assert.Contains("public string DisplayName { get; init; }", code);
        Assert.Contains("public string Name { get => DisplayName; init => DisplayName = value; }", code);
        Assert.Contains("public string Code { get; init; }", code);
        Assert.DoesNotContain("public string EID { get; init; }", code);
        Assert.DoesNotContain("public string ENAME { get; init; }", code);
        Assert.DoesNotContain("public string ECODE { get; init; }", code);
    }

    [Fact]
    public void Generate_UsesExternalClrTypeMappingForEnumFields()
    {
        var definition = new ModelDefinition(
            "Person",
            "Person",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("Status", "Status", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false)
            },
            Array.Empty<IndexDefinition>());

        var code = GenerateCode(definition, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = "OrderStatus"
        });

        Assert.Contains("public OrderStatus Status { get; init; }", code);
        Assert.Contains("public string? Status_DisplayValue { get; init; }", code);
    }

    [Fact]
    public void Generate_ForIsEnabledBooleanField_KeepsEnabledDefault()
    {
        var definition = new ModelDefinition(
            "Person",
            "Person",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false)
            },
            Array.Empty<IndexDefinition>());

        var code = GenerateCode(definition);

        Assert.Contains("public bool IsEnabled { get; init; } = true;", code);
    }

    [Theory]
    [InlineData("int", "int")]
    [InlineData("long", "long")]
    public void Generate_ForIntegerField_UsesDeclaredClrStorageType(string storeType, string expectedClrType)
    {
        var definition = new ModelDefinition(
            "Person",
            "Person",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("Revision", "Revision", FieldKind.Scalar, FieldValueType.Integer, storeType, true, false, false)
            },
            Array.Empty<IndexDefinition>());

        var code = GenerateCode(definition);

        Assert.Contains($"public {expectedClrType} Revision {{ get; init; }}", code);
    }

    [Fact]
    public void Generate_ForEmploymentStatusEnumField_KeepsFullTimeDefault()
    {
        var definition = new ModelDefinition(
            "Position",
            "Position",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("EmploymentStatus", "EmploymentStatus", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false)
            },
            Array.Empty<IndexDefinition>());

        var code = GenerateCode(definition, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EmploymentStatus"] = "EmploymentStatus"
        });

        Assert.Contains("public EmploymentStatus EmploymentStatus { get; init; } = (EmploymentStatus)1;", code);
    }

    private static string GenerateCode(ModelDefinition definition)
    {
        var generator = new ICP.Platform.Modeling.Dto.ModelDtoGenerator();
        return generator.Generate(definition);
    }

    private static string GenerateCode(
        ModelDefinition definition,
        IReadOnlyDictionary<string, string>? enumClrTypeMap)
    {
        var generator = new ICP.Platform.Modeling.Dto.ModelDtoGenerator();
        return generator.Generate(definition, enumClrTypeMap);
    }
}
