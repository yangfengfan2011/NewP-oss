using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PersistenceModelDigestTests
{
    [Fact]
    public void Compute_V1CrossOwnerPrefixOrderingRetainsFrozenBytes()
    {
        var field = new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId,
            "nvarchar(128)", true, true, false);
        var alpha = new ModelDefinition("Alpha", "Alpha", ModelKind.Entity, [field], [], "A.X");
        var zulu = new ModelDefinition("Zulu", "Zulu", ModelKind.Entity, [field], [], "A");

        // 冻结 v1 canonical 字节中 A.X/Alpha 排在 A/Zulu 之前，'.' 的 Ordinal 小于 '/'。
        const string expected = "0bb022d2b670216110c4100b26994df480f017b11e61a310a5a1177a5b93b6dc";
        Assert.Equal(expected, PersistenceModelDigest.Compute([zulu, alpha]));
        Assert.Equal(expected, PersistenceModelDigest.Compute([alpha, zulu]));
    }

    [Fact]
    public void Compute_CurrentFoundationAuthoringRetainsPublishedV1Digest()
    {
        Assert.Equal("56554470f57d6620d947482f760982f3f6c2473739c2c66696019fefc5aa21a1",
            PersistenceModelDigest.Compute(ICP.Platform.Persistence.Composition.PlatformFoundationModelSource.GetDefinitions()));
    }

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
