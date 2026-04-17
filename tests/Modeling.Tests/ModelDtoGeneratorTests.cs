using System.Reflection;
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

    private static string GenerateCode(ModelDefinition definition)
    {
        var generatorType = Type.GetType("ICP.Platform.Modeling.Dto.ModelDtoGenerator, PlatformModeling");
        Assert.NotNull(generatorType);

        var generator = Activator.CreateInstance(generatorType!);
        Assert.NotNull(generator);

        var method = generatorType!.GetMethod("Generate", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        return (string)method!.Invoke(generator, [definition])!;
    }
}
