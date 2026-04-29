using ICP.Platform.Persistence.DevSync.Models;
using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Persistence.DevSync.Schema;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Schema;

public sealed class TargetSchemaBuilderTests
{
    [Fact]
    public void LoadFromFile_DeserializesHistoryNameForModelAndField()
    {
        var path = CreateModelFile(
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "ConfigItem": {
                "kind": "Entity",
                "historyName": "config_items",
                "fields": [
                  {
                    "name": "DisplayName",
                    "historyName": "display_name",
                    "storage": {
                      "type": "string"
                    }
                  }
                ]
              }
            }
            """);

        try
        {
            var document = new SingleRootModelDocumentLoader().LoadFromFile(path);

            Assert.Equal("config_items", document.Model.HistoryName);
            Assert.Equal("display_name", Assert.Single(document.Model.Fields).HistoryName);
        }
        finally
        {
            DeleteModelFile(path);
        }
    }

    [Fact]
    public void Build_IncludesHistoryNameBuiltinEidAndBuiltinIndex()
    {
        var path = CreateModelFile(
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "ConfigItem": {
                "kind": "Entity",
                "historyName": "config_items",
                "fields": [
                  {
                    "name": "DisplayName",
                    "historyName": "display_name",
                    "storage": {
                      "type": "string"
                    }
                  }
                ]
              }
            }
            """);

        try
        {
            var document = new SingleRootModelDocumentLoader().LoadFromFile(path);
            var schema = new TargetSchemaBuilder(RepositoryRootLocator.Find())
                .Build(
                [
                    new DiscoveredModelDocument
                    {
                        ModuleId = document.ModuleId,
                        ModelName = document.Model.Name,
                        FilePath = path,
                        Document = document
                    }
                ]);

            var table = Assert.Single(schema.Tables);
            Assert.Equal("config_items", table.HistoryName);
            Assert.Contains(table.Columns, static column => column.Name == "DisplayName" && column.HistoryName == "display_name");
            Assert.Contains(table.Columns, static column => column.Name == "EID");
            Assert.Contains(table.Indexes, static index => index.Name == "UX_ConfigItem_EID");
        }
        finally
        {
            DeleteModelFile(path);
        }
    }

    private static string CreateModelFile(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dev-schema-sync-tests-{Guid.NewGuid():N}");
        var directory = Path.Combine(root, "modules", "ConfigCenter", "metadata", "models");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "ConfigItem.model.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static void DeleteModelFile(string path)
    {
        var modelsDirectory = Directory.GetParent(path)
            ?? throw new InvalidOperationException($"无法定位模型文件目录: {path}");
        var root = Directory.GetParent(modelsDirectory.FullName)?.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException($"无法定位测试临时根目录: {path}");

        if (Directory.Exists(root.FullName))
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }
}
