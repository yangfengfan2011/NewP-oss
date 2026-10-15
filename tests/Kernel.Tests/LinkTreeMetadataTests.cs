using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class LinkTreeMetadataTests
{
    private static JsonObject View() => JsonNode.Parse("""
        {"mode":"tree","providerKey":"platform.relations","linkSource":{"resourceKey":"workspace.content","nodeType":"folder","modelKey":"WorkspaceFolder","labelColumn":"Name","orderField":"TreeOrdinal","rootPolicyKey":"workspace.spaces","rootContextMenuKeys":["paste"]},
         "nodeTypes":[{"key":"folder","contextMenu":[{"key":"paste"}]}]}
        """)!.AsObject();

    [Theory]
    [InlineData("platform.relations")]
    [InlineData("workspace.spaces")]
    public void LinkSource_SupportsTheStandardProviderAndControlledEntryWrappers(string provider)
    {
        var view = View(); view["providerKey"] = provider;
        Assert.Equal("workspace.content", LinkTreeMetadata.Read(JsonSerializer.SerializeToElement(view)).ResourceKey);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("unknown")]
    [InlineData("field")]
    [InlineData("type")]
    [InlineData("menu")]
    [InlineData("duplicate-menu")]
    [InlineData("mode")]
    public void LinkSource_RejectsAmbiguousOrExecutableStaticInputs(string error)
    {
        var view = View();
        switch (error)
        {
            case "both": view["relationSource"] = new JsonObject(); break;
            case "unknown": view["linkSource"]!["sql"] = "SELECT 1"; break;
            case "field": view["linkSource"]!["orderField"] = "TreeOrdinal;SELECT 1"; break;
            case "type": view["nodeTypes"]![0]!["key"] = "other"; break;
            case "menu": view["linkSource"]!["rootContextMenuKeys"] = new JsonArray("unknown"); break;
            case "duplicate-menu": view["linkSource"]!["rootContextMenuKeys"] = new JsonArray("paste", "paste"); break;
            case "mode": view["mode"] = "list"; break;
        }
        Assert.Throws<JsonException>(() => LinkTreeMetadata.Read(JsonSerializer.SerializeToElement(view)));
    }
}
