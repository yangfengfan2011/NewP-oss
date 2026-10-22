using ICP.Platform.Plugin.Contracts;
using ICP.Platform.Plugin.Sdk;

namespace ICP.Platform.PluginApi.Tests;

public sealed class StandardPluginBlueprintCompilerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("newp_blueprint_").FullName;

    [Fact]
    public void Compile_SdkSerializationOfOmittedDeclarations_PreservesSupportedInputShape()
    {
        var compiler = new StandardPluginBlueprintCompiler();
        var original = compiler.Compile(WriteBlueprint(ValidBlueprint()));
        Assert.True(original.IsSuccess);
        var json = System.Text.Json.JsonSerializer.Serialize(original.Compilation!.Blueprint, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var replay = compiler.Compile(WriteBlueprint(json));
        Assert.True(replay.IsSuccess, string.Join(Environment.NewLine, replay.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Theory]
    [InlineData("\"dependencies\": null", "/dependencies")]
    [InlineData("\"dependencies\": [\"\"]", "/dependencies")]
    [InlineData("\"composition\": {}", "/composition")]
    [InlineData("\"composition\": { \"members\": null }", "/composition")]
    public void Compile_InvalidDeclarationShape_RejectsAtAuthoringBoundary(string declaration, string pointer)
    {
        var json = ValidBlueprint().Replace("\"standardData\":", declaration + ", \"standardData\":", StringComparison.Ordinal);
        var result = new StandardPluginBlueprintCompiler().Compile(WriteBlueprint(json));
        Assert.False(result.IsSuccess);
        Assert.Equal(pointer, Assert.Single(result.Diagnostics).JsonPointer);
    }

    [Fact]
    public void Compile_ExplicitDependenciesAndComposition_PreservesAuthoringForCanonicalConsumer()
    {
        var json = ValidBlueprint().Replace("\"standardData\":", "\"dependencies\": [\"Part\"], \"composition\": { \"members\": [\"Partner.AssetCatalog.Member\"] }, \"standardData\":", StringComparison.Ordinal);

        var result = new StandardPluginBlueprintCompiler().Compile(WriteBlueprint(json));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var serialized = System.Text.Json.JsonSerializer.SerializeToElement(result.Compilation!.Blueprint, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal("Part", serialized.GetProperty("dependencies")[0].GetString());
        Assert.Equal("Partner.AssetCatalog.Member", serialized.GetProperty("composition").GetProperty("members")[0].GetString());
    }

    [Fact]
    public void Compile_AssetBlueprint_DerivesCanonicalStandardDataProjection()
    {
        var path = WriteBlueprint(ValidBlueprint());

        var result = new StandardPluginBlueprintCompiler().Compile(path);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        var compilation = Assert.IsType<StandardPluginCompilation>(result.Compilation);
        Assert.Equal("Partner.AssetCatalog", compilation.Blueprint.ModuleId);
        Assert.Equal(PlatformApiReleaseIdentity.Version, compilation.Attestation.BuiltWith);
        Assert.Equal(PlatformApiReleaseIdentity.ReleaseDigest, compilation.Attestation.ReleaseDigest);
        var resource = Assert.Single(compilation.Projection.Resources);
        Assert.Equal("/api/plugins/partner-asset-catalog/assets", resource.ApiRoute);
        Assert.Equal("/plugins/partner-asset-catalog/assets", resource.PageRoute);
        Assert.Equal("np_partner_asset_catalog", resource.SchemaName);
        Assert.Equal("Assets", resource.TableName);
        Assert.Equal("partner.asset.detail", resource.DetailViewKey);
        Assert.Equal("UX_Assets_Code", Assert.Single(resource.UniqueIndexes));
    }

    [Theory]
    [InlineData("custom-code.v1", "PLUGIN_PROFILE_UNSUPPORTED")]
    [InlineData("standard-data.v2", "PLUGIN_PROFILE_UNSUPPORTED")]
    public void Compile_UnsupportedProfile_FailsClosed(string profile, string expectedCode)
    {
        var path = WriteBlueprint(ValidBlueprint().Replace("standard-data.v1", profile, StringComparison.Ordinal));

        var result = new StandardPluginBlueprintCompiler().Compile(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Compile_BackendAndFrontendRuntimeEntries_AreRejected()
    {
        var json = ValidBlueprint().Replace(
            "\"standardData\"",
            "\"backend\": { \"entryAssembly\": \"Partner.AssetCatalog.dll\" },\n  \"frontend\": { \"entry\": \"dist/index.js\" },\n  \"standardData\"",
            StringComparison.Ordinal);
        var path = WriteBlueprint(json);

        var result = new StandardPluginBlueprintCompiler().Compile(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            new[]
            {
                PlatformApiDiagnosticCodes.StandardProfileBackendCodeForbidden,
                PlatformApiDiagnosticCodes.StandardProfileFrontendCodeForbidden
            },
            result.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Compile_MinimumBeforeBuiltWith_IsRejected()
    {
        var path = WriteBlueprint(ValidBlueprint().Replace("\"minInclusive\": \"1.0.0\"", "\"minInclusive\": \"0.9.0\"", StringComparison.Ordinal));

        var result = new StandardPluginBlueprintCompiler().Compile(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlatformApiDiagnosticCodes.PlatformApiIncompatible, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Compile_EmptyDeclaredDetailViewKey_IsRejected()
    {
        var path = WriteBlueprint(ValidBlueprint().Replace(
            "\"detailViewKey\": \"partner.asset.detail\"",
            "\"detailViewKey\": \"\"",
            StringComparison.Ordinal));

        var result = new StandardPluginBlueprintCompiler().Compile(path);

        Assert.False(result.IsSuccess);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PlatformApiDiagnosticCodes.PackageSchemaUnsupported, diagnostic.Code);
        Assert.Equal("/standardData/resources/0/detailViewKey", diagnostic.JsonPointer);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string WriteBlueprint(string json)
    {
        var path = Path.Combine(_root, "newp.plugin.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string ValidBlueprint() =>
        """
        {
          "schemaVersion": "1.0",
          "profile": "standard-data.v1",
          "moduleId": "Partner.AssetCatalog",
          "displayName": "资产目录",
          "version": "1.0.0",
          "description": "Partner-owned asset catalog",
          "compatibility": {
            "platformApi": {
              "minInclusive": "1.0.0",
              "maxExclusive": "2.0.0"
            },
            "persistence": {
              "contract": "ef-migrations.v1",
              "providers": ["sqlServer"]
            }
          },
          "standardData": {
            "resources": [
              {
                "key": "Asset",
                "pluralName": "Assets",
                "displayName": "资产",
                "displayField": "Name",
                "detailViewKey": "partner.asset.detail",
                "fields": [
                  { "name": "Code", "type": "string", "required": true, "unique": true },
                  { "name": "Name", "type": "string", "required": true },
                  { "name": "PurchasedAt", "type": "date", "required": false }
                ]
              }
            ]
          },
          "navigation": {
            "slot": "extensions",
            "group": "扩展示例",
            "menu": "资产目录",
            "order": 100
          },
          "persistence": {
            "models": ["metadata/models/Asset.model.json"],
            "migrations": {
              "sqlServer": {
                "assembly": "migrations/sqlserver/Partner.AssetCatalog.Migrations.dll",
                "manifest": "migrations/sqlserver/migrations.json"
              }
            }
          }
        }
        """;
}
