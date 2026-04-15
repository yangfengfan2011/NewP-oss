using System;
using System.IO;
using System.Text.Json;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModelMetadataProviderTests
{
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
        Assert.Contains(definition.Fields, static x => x.Name == "EID" && x.IsPrimaryKey);
        Assert.Contains(definition.Fields, static x => x.Name == "ENAME");
        Assert.Contains(definition.Fields, static x => x.Name == "Key");
        Assert.Contains(definition.Fields, static x => x.Name == "Value");
        Assert.Contains(definition.Fields, static x => x.Name == "Type");
        Assert.Contains(definition.Fields, static x => x.Name == "ModuleId");
        Assert.Contains(definition.Fields, static x => x.Name == "Group");
        Assert.Contains(definition.Fields, static x => x.Name == "Environment");
        Assert.Contains(definition.Fields, static x => x.Name == "TenantId");
        Assert.Contains(definition.Fields, static x => x.Name == "Description");
        Assert.Contains(definition.Fields, static x => x.Name == "IsSensitive");
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
