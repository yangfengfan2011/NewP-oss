using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PersistenceModelDigestTests
{
    [Fact]
    public void Compute_CompositeIndexFieldOrderChanges_DigestChanges()
    {
        var first = BuildModel(["Code", "Name"]);
        var second = BuildModel(["Name", "Code"]);

        var firstDigest = PersistenceModelDigest.Compute([first]);
        var secondDigest = PersistenceModelDigest.Compute([second]);

        Assert.NotEqual(firstDigest, secondDigest);
    }

    [Fact]
    public void Compute_DisplayRuleChanges_DigestDoesNotChange()
    {
        var withoutDisplayRule = BuildModel(["Code", "Name"]);
        var withDisplayRule = withoutDisplayRule with
        {
            Fields = withoutDisplayRule.Fields
                .Select(field => field.Name == "Code"
                    ? field with
                    {
                        DisplayRule = new FieldDisplayRuleDefinition(
                            "reference",
                            "Partner",
                            "Id",
                            "Name")
                    }
                    : field)
                .ToArray()
        };

        Assert.Equal(
            PersistenceModelDigest.Compute([withoutDisplayRule]),
            PersistenceModelDigest.Compute([withDisplayRule]));
    }

    [Fact]
    public void Compute_InstantAndDateTimeWithSameStorage_DigestDoNotChange()
    {
        var dateTimeModel = BuildModel(["Code", "Name"]);
        var instantModel = dateTimeModel with
        {
            Fields = dateTimeModel.Fields
                .Select(field => field.Name == "Code"
                    ? field with
                    {
                        ValueType = FieldValueType.Instant,
                        StoreType = "datetime"
                    }
                    : field)
                .ToArray()
        };
        dateTimeModel = dateTimeModel with
        {
            Fields = dateTimeModel.Fields
                .Select(field => field.Name == "Code"
                    ? field with
                    {
                        ValueType = FieldValueType.DateTime,
                        StoreType = "datetime"
                    }
                    : field)
                .ToArray()
        };

        Assert.Equal(
            PersistenceModelDigest.Compute([dateTimeModel]),
            PersistenceModelDigest.Compute([instantModel]));
    }

    [Fact]
    public void Compute_IncludeNullValuesChangesDigest()
    {
        var baseline = BuildModel(["Code", "Name"]);
        var explicitNullScope = baseline with
        {
            Indexes = baseline.Indexes
                .Select(index => index with { IncludeNullValues = true, IsUnique = true })
                .ToArray()
        };

        Assert.NotEqual(
            PersistenceModelDigest.Compute([baseline]),
            PersistenceModelDigest.Compute([explicitNullScope]));
    }

    private static ModelDefinition BuildModel(IReadOnlyList<string> indexFields) =>
        new(
            "Asset",
            "Asset",
            ModelKind.Entity,
            [
                new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "nvarchar(64)", true, true, false),
                new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "nvarchar(128)", true, false, false),
                new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "nvarchar(256)", true, false, false)
            ],
            [new IndexDefinition("IX_Asset_Code_Name", indexFields, IsUnique: false)],
            "Partner.AssetCatalog",
            "np_partner_assetcatalog");
}
