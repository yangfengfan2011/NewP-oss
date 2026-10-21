using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class DataScopeRuleMaterializerTests
{
    [Fact]
    public void CompleteTree_IsIndependentOfMutableInputsAndExposesOnlyReadOnlyCollections()
    {
        var rootRow = Node("root", "root", null, false);
        var childRow = Node("child", "root", "root");
        var subjectRow = Subject("vote", "child");
        var root = Assert.Single(DataScopeRuleMaterializer.Build([rootRow, childRow], [subjectRow]));
        rootRow["Name"] = "changed";
        childRow["ConditionJson"] = "{}";
        subjectRow["ActionsJson"] = "{\"read\":\"allow\"}";
        Assert.Equal("root", root.Name);
        Assert.False(root.Enabled);
        var child = Assert.Single(root.Children);
        Assert.Equal("keep", child.Condition.Children![0].Value.GetString());
        Assert.Equal("deny", Assert.Single(child.Subjects).Actions["read"]);
        Assert.Throws<NotSupportedException>(() => ((IList<DataScopeRuleDto>)root.Children).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DataScopeSubjectDto>)child.Subjects).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<DataScopeConditionDto>)child.Condition.Children!).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)child.Subjects[0].Actions)["read"] = "allow");
    }

    [Fact]
    public void EmptyCompleteCollection_IsValid()
    {
        Assert.Empty(DataScopeRuleMaterializer.Build([], []));
    }

    [Theory]
    [InlineData("other-root", "root")]
    [InlineData("root", "missing-parent")]
    [InlineData("root", "child")]
    public void UnreachableNodeOrInconsistentRoot_IsRejected(string rootId, string parent)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DataScopeRuleMaterializer.Build(
            [Node("root", "root", null), Node("child", rootId, parent)], []));
        Assert.Equal("DATA_SCOPE_TREE_INVALID", error.Message);
    }

    [Fact]
    public void SubjectOutsideCompleteNodeCollection_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DataScopeRuleMaterializer.Build(
            [Node("root", "root", null)], [Subject("orphan", "absent")]));
        Assert.Equal("DATA_SCOPE_SUBJECT_NODE_INVALID", error.Message);
    }

    [Fact]
    public void MalformedJson_IsNeverTreatedAsAnEmptyConfiguration()
    {
        var node = Node("root", "root", null);
        node["ConditionJson"] = "{";
        Assert.Throws<JsonException>(() => DataScopeRuleMaterializer.Build([node], []));
    }

    [Theory]
    [InlineData("""{"type":"group","field":"","operator":"and","value":null,"children":[null]}""")]
    [InlineData("""{"type":"group","field":"","operator":"and","value":null,"children":[{"type":"group","field":"","operator":"and","value":null,"children":[null]}]}""")]
    public void NullConditionChild_IsReportedAsInvalidConfiguration(string conditionJson)
    {
        var node = Node("root", "root", null, false);
        node["ConditionJson"] = conditionJson;
        var error = Assert.Throws<InvalidOperationException>(() => DataScopeRuleMaterializer.Build([node], []));
        Assert.Equal("DATA_SCOPE_CONDITION_INVALID", error.Message);
    }

    private static Dictionary<string, object?> Node(string id, string root, string? parent, bool enabled = true) => new()
    {
        ["EID"] = id, ["RootId"] = root, ["ParentId"] = parent, ["Name"] = id, ["ResourceKey"] = "organization.person", ["Enabled"] = enabled,
        ["ConditionJson"] = JsonSerializer.Serialize(new DataScopeConditionDto("group", "", "and", JsonSerializer.SerializeToElement<object?>(null),
            Children: [new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("keep"))]), new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };

    private static Dictionary<string, object?> Subject(string id, string node) => new()
    {
        ["EID"] = id, ["NodeId"] = node, ["SubjectType"] = "all", ["SubjectId"] = null, ["Priority"] = 1L,
        ["ActionsJson"] = "{\"read\":\"deny\"}", ["MatchOptionsJson"] = null
    };
}
