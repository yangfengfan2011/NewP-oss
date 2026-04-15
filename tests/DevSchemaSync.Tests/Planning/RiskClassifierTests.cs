using ICP.Platform.Tools.DevSchemaSync.Planning;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Planning;

public sealed class RiskClassifierTests
{
    [Theory]
    [InlineData(SchemaOperationKind.DropColumn, RiskLevel.Confirm)]
    [InlineData(SchemaOperationKind.RenameColumn, RiskLevel.Confirm)]
    [InlineData(SchemaOperationKind.AlterColumnType, RiskLevel.Confirm)]
    [InlineData(SchemaOperationKind.AlterNullability, RiskLevel.Confirm)]
    [InlineData(SchemaOperationKind.CreateTable, RiskLevel.Safe)]
    public void Classify_ReturnsExpectedRisk(string operationKind, string expectedRisk)
    {
        var classifier = new RiskClassifier();

        var risk = classifier.Classify(operationKind);

        Assert.Equal(expectedRisk, risk);
    }
}
