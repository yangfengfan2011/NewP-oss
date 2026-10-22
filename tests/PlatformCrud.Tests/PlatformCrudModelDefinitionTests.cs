using ICP.Platform.Crud.Tests.Support;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class PlatformCrudModelDefinitionTests
{
    [Fact]
    public void Person_ModelDefinition_UsesCurrentConstructorSignatures()
    {
        var definition = PlatformCrudModelDefinitions.Person();

        Assert.Equal("Person", definition.Name);
        Assert.Contains(definition.Fields, static field => field.Name == "EID" && field.IsPrimaryKey);
    }
}
