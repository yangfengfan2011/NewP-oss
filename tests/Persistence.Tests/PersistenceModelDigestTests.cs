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
