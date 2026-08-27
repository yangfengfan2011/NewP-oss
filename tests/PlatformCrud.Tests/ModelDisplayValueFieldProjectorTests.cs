using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class ModelDisplayValueFieldProjectorTests
{
    [Fact]
    public void Project_DerivesReferenceDisplayCompanionFromModelArtifact()
    {
        var source = PersonWithCompanyDisplayRule();
        var company = Company();

        var display = Assert.Single(new ModelDisplayValueFieldProjector().Project(source, [company]));

        Assert.Equal("CompanyId_DisplayValue", display.Field);
        Assert.Equal("CompanyId", display.SourceField);
        Assert.Equal("Company", display.TargetModelName);
        Assert.Equal("EID", display.TargetIdField);
        Assert.Equal("ENAME", display.TargetDisplayField);
    }

    [Fact]
    public void Project_DerivesUserDisplayCompanionFromModelArtifactWithoutTargetCatalogEntry()
    {
        var source = ExampleRecordWithReviewerDisplayRule();

        var display = Assert.Single(new ModelDisplayValueFieldProjector().Project(source, [source]));

        Assert.Equal("ReviewerUserEID_DisplayValue", display.Field);
        Assert.Equal("ReviewerUserEID", display.SourceField);
        Assert.Equal("IdentityAccount", display.TargetModelName);
        Assert.Equal("EID", display.TargetIdField);
        Assert.Equal("ENAME", display.TargetDisplayField);
    }

    [Fact]
    public void Project_ThrowsWhenTargetModelIsMissing()
    {
        var source = PersonWithCompanyDisplayRule();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ModelDisplayValueFieldProjector().Project(source, [source]));

        Assert.Contains("Company", ex.Message, StringComparison.Ordinal);
        Assert.Contains("target model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_ThrowsWhenTargetIdFieldIsMissing()
    {
        var source = PersonWithCompanyDisplayRule();
        var company = Company(includeId: false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ModelDisplayValueFieldProjector().Project(source, [company]));

        Assert.Contains("Company", ex.Message, StringComparison.Ordinal);
        Assert.Contains("EID", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ThrowsWhenTargetDisplayFieldIsMissing()
    {
        var source = PersonWithCompanyDisplayRule();
        var company = Company(includeDisplay: false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ModelDisplayValueFieldProjector().Project(source, [company]));

        Assert.Contains("Company", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ENAME", ex.Message, StringComparison.Ordinal);
    }

    private static ModelDefinition PersonWithCompanyDisplayRule() => new(
        Name: "Person",
        TableName: "Person",
        Kind: ModelKind.Entity,
        Fields:
        [
            new FieldDefinition(
                "CompanyId",
                "CompanyId",
                FieldKind.Reference,
                FieldValueType.ObjectId,
                "guid",
                false,
                false,
                false,
                new FieldDisplayRuleDefinition("reference", "Company", "EID", "ENAME"))
        ],
        Indexes: []);

    private static ModelDefinition ExampleRecordWithReviewerDisplayRule() => new(
        Name: "ExampleRecord",
        TableName: "ExampleRecord",
        Kind: ModelKind.Entity,
        Fields:
        [
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
        ],
        Indexes: []);

    private static ModelDefinition Company(bool includeId = true, bool includeDisplay = true)
    {
        var fields = new List<FieldDefinition>();
        if (includeId)
        {
            fields.Add(new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false));
        }

        if (includeDisplay)
        {
            fields.Add(new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false));
        }

        return new ModelDefinition(
            Name: "Company",
            TableName: "Company",
            Kind: ModelKind.Entity,
            Fields: fields,
            Indexes: []);
    }
}
