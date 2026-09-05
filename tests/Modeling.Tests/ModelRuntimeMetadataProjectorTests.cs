using System.Text;
using System.Text.Json;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public sealed class ModelRuntimeMetadataProjectorTests
{
    [Fact]
    public void Project_CompletesEntityFixedFieldCatalogAndPreservesExplicitAuthoring()
    {
        var authoring = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "Company": {
                "kind": "Entity",
                "customFact": "preserved",
                "sections": [{ "key": "basic", "fields": ["EID", "IsEnabled"] }],
                "fields": [
                  {
                    "name": "EID",
                    "form": {
                      "create": { "mode": "editable" },
                      "edit": { "mode": "editable" }
                    }
                  },
                  {
                    "name": "CreatedAt",
                    "uiType": "readonly",
                    "form": { "create": { "mode": "hidden" } }
                  },
                  {
                    "name": "UpdatedAt",
                    "layout": { "span": 12 },
                    "customFieldFact": "preserved",
                    "form": { "edit": { "mode": "readonly" } }
                  },
                  {
                    "name": "IsEnabled",
                    "storage": { "type": "bool" },
                    "form": {
                      "create": { "mode": "hidden" },
                      "edit": { "mode": "editable" }
                    }
                  }
                ]
              }
            }
            """);
        var unchangedAuthoring = authoring.ToArray();
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            "runtime-model-projector",
            "modules",
            "Organization",
            "metadata",
            "models",
            "Company.model.json");

        var runtimeBytes = new ModelRuntimeMetadataProjector().Project(authoring, sourcePath);

        Assert.Equal(unchangedAuthoring, authoring);
        using var runtime = JsonDocument.Parse(runtimeBytes);
        var model = runtime.RootElement.GetProperty("Company");
        Assert.Equal("preserved", model.GetProperty("customFact").GetString());
        Assert.Equal("basic", model.GetProperty("sections")[0].GetProperty("key").GetString());
        var fields = model.GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal(6, fields.Length);

        var explicitKey = Field(fields, "EID");
        Assert.Equal("editable", explicitKey.GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
        Assert.Equal("editable", explicitKey.GetProperty("form").GetProperty("edit").GetProperty("mode").GetString());

        var createdAt = Field(fields, "CreatedAt");
        Assert.Equal("readonly", createdAt.GetProperty("uiType").GetString());
        Assert.Equal("hidden", createdAt.GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
        Assert.Equal("hidden", createdAt.GetProperty("form").GetProperty("edit").GetProperty("mode").GetString());

        var updatedAt = Field(fields, "UpdatedAt");
        Assert.Equal("dateTime", updatedAt.GetProperty("uiType").GetString());
        Assert.Equal("dateTime", updatedAt.GetProperty("contract").GetProperty("valueType").GetString());
        Assert.Equal("datetime", updatedAt.GetProperty("storage").GetProperty("type").GetString());
        Assert.False(updatedAt.GetProperty("storage").GetProperty("nullable").GetBoolean());
        Assert.Equal(12, updatedAt.GetProperty("layout").GetProperty("span").GetInt32());
        Assert.Equal("preserved", updatedAt.GetProperty("customFieldFact").GetString());
        Assert.Equal("hidden", updatedAt.GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
        Assert.Equal("readonly", updatedAt.GetProperty("form").GetProperty("edit").GetProperty("mode").GetString());

        var isEnabled = Field(fields, "IsEnabled");
        Assert.Equal("hidden", isEnabled.GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
        Assert.Equal("editable", isEnabled.GetProperty("form").GetProperty("edit").GetProperty("mode").GetString());

        foreach (var synthesizedAuditField in new[] { "CreatedBy", "UpdatedBy" })
        {
            var field = Field(fields, synthesizedAuditField);
            Assert.Equal("user", field.GetProperty("contract").GetProperty("valueType").GetString());
            Assert.Equal("string", field.GetProperty("storage").GetProperty("type").GetString());
            Assert.Equal("hidden", field.GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
            Assert.Equal("hidden", field.GetProperty("form").GetProperty("edit").GetProperty("mode").GetString());
        }
    }

    [Fact]
    public void Project_AppendsOptionalEntityBuiltinFieldsAsHiddenCatalogEntries()
    {
        var runtimeBytes = Project(
            "EntityCatalog",
            """
            "kind": "Entity",
            "builtinFields": ["ENAME", "ECODE"],
            "sections": [{ "key": "basic", "fields": ["BusinessValue", "ENAME", "ECODE"] }],
            "fields": [
              {
                "name": "BusinessValue",
                "storage": { "type": "string", "nullable": false },
                "dataSource": { "type": "provider", "key": "test.business-values" },
                "form": {
                  "create": { "mode": "editable" },
                  "edit": { "mode": "editable" }
                }
              },
              {
                "name": "ENAME",
                "dataSource": { "type": "provider", "key": "test.names" },
                "form": {
                  "create": { "mode": "hidden" },
                  "edit": { "mode": "hidden" }
                }
              },
              {
                "name": "ECODE",
                "contract": { "valueType": "enum", "clrType": "TestCode" },
                "dataSource": { "type": "enum", "key": "test.codes" },
                "form": {
                  "create": { "mode": "hidden" },
                  "edit": { "mode": "hidden" }
                }
              }
            ]
            """);

        using var runtime = JsonDocument.Parse(runtimeBytes);
        var model = runtime.RootElement.GetProperty("EntityCatalog");
        var fields = model.GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal("BusinessValue", fields[0].GetProperty("name").GetString());
        Assert.Equal(["BusinessValue", "ENAME", "ECODE"], model.GetProperty("sections")[0].GetProperty("fields").EnumerateArray().Select(static item => item.GetString()));
        var businessField = Field(fields, "BusinessValue");
        Assert.False(businessField.TryGetProperty("uiType", out _));
        Assert.Equal("provider", businessField.GetProperty("dataSource").GetProperty("type").GetString());
        Assert.Equal("editable", businessField.GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
        var displayName = Field(fields, "ENAME");
        Assert.False(displayName.TryGetProperty("uiType", out _));
        Assert.Equal("provider", displayName.GetProperty("dataSource").GetProperty("type").GetString());
        var code = Field(fields, "ECODE");
        Assert.False(code.TryGetProperty("uiType", out _));
        Assert.Equal("enum", code.GetProperty("contract").GetProperty("valueType").GetString());

        foreach (var fieldName in new[] { "EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
        {
            AssertHidden(Field(fields, fieldName));
        }
    }

    [Fact]
    public void Project_AppendsLinkCatalogWithoutChangingExplicitLidOwnershipOrForms()
    {
        var runtimeBytes = Project(
            "Membership",
            """
            "kind": "Link",
            "linkedTypes": [{ "parent": "Parent", "child": "Child" }],
            "builtinFields": ["LSequence"],
            "sections": [{ "key": "basic", "fields": ["LID", "RelationType"] }],
            "fields": [
              {
                "name": "LID",
                "form": {
                  "create": { "mode": "editable" },
                  "edit": { "mode": "editable" }
                }
              },
              {
                "name": "RelationType",
                "storage": { "type": "string", "nullable": false },
                "form": {
                  "create": { "mode": "editable" },
                  "edit": { "mode": "editable" }
                }
              }
            ]
            """);

        using var runtime = JsonDocument.Parse(runtimeBytes);
        var model = runtime.RootElement.GetProperty("Membership");
        var fields = model.GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal("LID", fields[0].GetProperty("name").GetString());
        Assert.Equal("RelationType", fields[1].GetProperty("name").GetString());
        Assert.Equal("editable", Field(fields, "LID").GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
        Assert.Equal("editable", Field(fields, "RelationType").GetProperty("form").GetProperty("edit").GetProperty("mode").GetString());

        foreach (var fieldName in new[] { "PEID", "CEID", "LCreatedBy", "LCreatedAt", "LUpdatedBy", "LUpdatedAt", "LSequence" })
        {
            AssertHidden(Field(fields, fieldName));
        }
    }

    private static byte[] Project(string modelName, string modelBody)
    {
        var authoring = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schemaVersion": "1.0",
              "type": "model",
              "{{modelName}}": {
                {{modelBody}}
              }
            }
            """);
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            "runtime-model-projector",
            "modules",
            "TestModule",
            "metadata",
            "models",
            $"{modelName}.model.json");
        return new ModelRuntimeMetadataProjector().Project(authoring, sourcePath);
    }

    private static void AssertHidden(JsonElement field)
    {
        Assert.Equal("hidden", field.GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
        Assert.Equal("hidden", field.GetProperty("form").GetProperty("edit").GetProperty("mode").GetString());
    }

    private static JsonElement Field(JsonElement[] fields, string name) =>
        Assert.Single(fields, field => field.GetProperty("name").GetString() == name);
}
