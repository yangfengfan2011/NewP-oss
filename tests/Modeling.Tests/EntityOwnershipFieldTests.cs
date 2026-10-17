using System.Text;
using System.Text.Json;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Ownership;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public sealed class EntityOwnershipFieldTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionalOwnershipFields_AreProjectedOnlyWhenSelected_AndAreServerOwned(bool selected)
    {
        var json = Json(selected);
        var document = new JsonModelMetadataDocumentLoader().Load(Encoding.UTF8.GetBytes(json), "modules/Workspace/metadata/models/WorkspaceFolder.model.json");
        new ModelMetadataValidator().Validate(document);
        var model = new ModelMetadataProjector().Project(document);
        foreach (var name in new[] { "OwnerId", "OwnerDepartmentId" })
        {
            var field = model.Fields.SingleOrDefault(field => field.Name == name);
            Assert.Equal(selected, field is not null);
            if (field is null) continue;
            Assert.False(field.Required);
            Assert.False(field.IsAuditField);
            Assert.True(PlatformModelFieldOwnership.IsServerOwned(field, hasExplicitAuthoring: true));
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var serialized = JsonSerializer.Serialize(field, options);
            using var serializedDocument = JsonDocument.Parse(serialized);
            Assert.True(serializedDocument.RootElement.GetProperty("isOwnershipField").GetBoolean());
            Assert.True(JsonSerializer.Deserialize<FieldDefinition>(serialized, options)!.IsOwnershipField);
        }
        using var runtime = JsonDocument.Parse(new ModelRuntimeMetadataProjector().Project(Encoding.UTF8.GetBytes(json), "modules/Workspace/metadata/models/WorkspaceFolder.model.json"));
        var fields = runtime.RootElement.GetProperty("WorkspaceFolder").GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal(selected ? 2 : 0, fields.Count(field => field.GetProperty("name").GetString()!.StartsWith("Owner", StringComparison.Ordinal)));
    }

    [Fact]
    public void LegacyCanonical_WithoutOwnershipMarker_RoundTripsWithIdenticalModelJson()
    {
        var document = new JsonModelMetadataDocumentLoader().Load(Encoding.UTF8.GetBytes(Json(false)), "modules/Workspace/metadata/models/WorkspaceFolder.model.json");
        var model = new ModelMetadataProjector().Project(document);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // 已发布字段契约没有本次新增的标记；旧 canonical 字节必须仍可严格验证。
        var legacy = JsonSerializer.SerializeToNode(model, options)!;
        foreach (var field in legacy["fields"]!.AsArray()) field!.AsObject().Remove("isOwnershipField");
        var canonical = legacy.ToJsonString(options);

        var restored = JsonSerializer.Deserialize<ModelDefinition>(canonical, options)!;

        Assert.All(restored.Fields, field => Assert.False(field.IsOwnershipField));
        Assert.Equal(canonical, JsonSerializer.Serialize(restored, options));
    }

    [Fact]
    public void OwnershipField_CannotBeMadeEditableByAuthoring()
    {
        var json = Json(true).Replace("\"fields\": []", "\"fields\": [{\"name\":\"OwnerId\",\"form\":{\"edit\":{\"mode\":\"editable\"}}}]");
        var document = new JsonModelMetadataDocumentLoader().Load(Encoding.UTF8.GetBytes(json), "modules/Workspace/metadata/models/WorkspaceFolder.model.json");
        var failure = Assert.Throws<InvalidOperationException>(() => new ModelMetadataValidator().Validate(document));
        Assert.Contains("OwnerId", failure.Message);
        Assert.Contains("editable", failure.Message);
    }

    private static string Json(bool selected) => "{\"schemaVersion\":\"1.0\",\"type\":\"model\",\"WorkspaceFolder\":{\"kind\":\"Entity\",\"builtinFields\":"
        + (selected ? "[\"OwnerId\",\"OwnerDepartmentId\"]" : "[]") + ",\"fields\": []}}";
}
