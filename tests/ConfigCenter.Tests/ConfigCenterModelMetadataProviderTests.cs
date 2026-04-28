using System;
using System.IO;
using System.Text.Json;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModelMetadataProviderTests
{
    [Fact]
    public void GetDefinitions_ReturnsConfigItemAndValueListItem_AndValidatesThem()
    {
        var provider = new ConfigCenterModelMetadataProvider();
        var definitions = provider.GetDefinitions();

        Assert.Equal(["ConfigItem", "ValueListItem"], definitions.Select(definition => definition.Name).ToArray());

        var validator = new ModelDefinitionValidator();
        foreach (var definition in definitions)
        {
            validator.Validate(definition);
        }
    }

    [Fact]
    public void LoadConfigItem_ReadsModelFromUnifiedMetadataLayout()
    {
        var repositoryRoot = GetRepositoryRoot();
        var newModelPath = Path.Combine(
            repositoryRoot,
            "modules",
            "ConfigCenter",
            "metadata",
            "models",
            "ConfigItem.model.json");
        var legacyModelPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "ConfigCenter",
            "metadata",
            "models",
            "ConfigItem.model.json");

        Assert.True(File.Exists(newModelPath), $"应能在新布局中找到模型文件：{newModelPath}");
        Assert.False(File.Exists(legacyModelPath), $"旧路径不应再作为权威源：{legacyModelPath}");

        var provider = new ConfigCenterModelMetadataProvider();

        var definition = provider.LoadConfigItem();

        Assert.Equal("ConfigItem", definition.Name);
        Assert.Equal("ConfigItem", definition.TableName);
        Assert.Contains(definition.Fields, static x => x.Name == "EID" && x.IsPrimaryKey);
        Assert.Contains(definition.Fields, static x => x.Name == "ENAME");
        Assert.Contains(definition.Fields, static x => x.Name == "Key" && x.StoreName == "Key");
        Assert.Contains(definition.Fields, static x => x.Name == "Value" && x.StoreName == "Value");
        Assert.Contains(definition.Fields, static x => x.Name == "Type" && x.StoreName == "Type");
        Assert.Contains(definition.Fields, static x => x.Name == "ModuleId" && x.StoreName == "ModuleId");
        Assert.Contains(definition.Fields, static x => x.Name == "Group" && x.StoreName == "Group");
        Assert.Contains(definition.Fields, static x => x.Name == "Environment" && x.StoreName == "Environment");
        Assert.Contains(definition.Fields, static x => x.Name == "TenantId" && x.StoreName == "TenantId");
        Assert.Contains(definition.Fields, static x => x.Name == "Description" && x.StoreName == "Description");
        Assert.Contains(definition.Fields, static x => x.Name == "IsSensitive" && x.StoreName == "IsSensitive");
    }

    [Fact]
    public void LoadConfigItem_WhenModelLivesOutsideModelsDirectory_StillReadsBySharedLoader()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"config-center-model-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var modelPath = Path.Combine(tempRoot, "modules", "ConfigCenter", "metadata", "domain", "ConfigItem.model.json");
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
            var repositoryRoot = GetRepositoryRoot();
            var sampleModelPath = Path.Combine(
                repositoryRoot,
                "modules",
                "ConfigCenter",
                "metadata",
                "models",
                "ConfigItem.model.json");
            File.WriteAllText(modelPath, File.ReadAllText(sampleModelPath));
            var storageTypeMapPath = Path.Combine(repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json");
            var provider = new ConfigCenterModelMetadataProvider(
                modelPath,
                storageTypeMapPath,
                new JsonModelMetadataDocumentLoader());

            var definition = provider.LoadConfigItem();

            Assert.Equal("ConfigItem", definition.Name);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ConfigItemLocalizationKeys_AreBackfilledIntoConfigCenterDictionary()
    {
        var repositoryRoot = GetRepositoryRoot();
        var dictionaryPath = Path.Combine(
            repositoryRoot,
            "modules",
            "Platform",
            "metadata",
            "dictionaries",
            "ConfigCenter.dic.json");

        Assert.True(File.Exists(dictionaryPath), $"应能在平台字典目录中找到 ConfigCenter.dic.json：{dictionaryPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(dictionaryPath));
        var configCenterRoot = document.RootElement.GetProperty("ConfigCenter");

        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.EID");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.ENAME");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.Key");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.Value");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.Type");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.ModuleId");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.Group");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.Environment");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.TenantId");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.Description");
        AssertLocalizedEntry(configCenterRoot, "Model.ConfigItem.Field.IsSensitive");
    }

    [Fact]
    public void SqlServerSnapshot_UsesPascalCaseTableAndColumnNames_ForConfigItem()
    {
        var repositoryRoot = GetRepositoryRoot();
        var snapshotPath = Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs");

        var snapshot = File.ReadAllText(snapshotPath);

        Assert.Contains("b.ToTable(\"ConfigItem\"", snapshot, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"Key\")", snapshot, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"ModuleId\")", snapshot, StringComparison.Ordinal);
        Assert.Contains("b.ToTable(\"ValueListItem\"", snapshot, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"Name\")", snapshot, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"UpdatedBy\")", snapshot, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    private static void AssertLocalizedEntry(JsonElement dictionaryRoot, string key)
    {
        var entry = dictionaryRoot.GetProperty(key);
        Assert.True(entry.TryGetProperty("zhs", out var zhs) && !string.IsNullOrWhiteSpace(zhs.GetString()), $"词条 {key} 缺少 zhs。");
        Assert.True(entry.TryGetProperty("en", out var en) && !string.IsNullOrWhiteSpace(en.GetString()), $"词条 {key} 缺少 en。");
    }
}
