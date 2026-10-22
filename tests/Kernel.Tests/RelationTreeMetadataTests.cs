using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class RelationTreeMetadataTests
{
    [Fact]
    public void Definition_AcceptsSelfReferencingRootsAndRejectsDisconnectedTypes()
    {
        var node = new RelationTreeNodeSource
        {
            Key = "department", Model = "Department", ResourceKey = "organization.department",
            IdField = "EID", LabelField = "ENAME", CodeField = "ECODE", SortField = "SortOrder",
            RootWhenParentNull = true, Parents = [new() { NodeType = "department", Field = "ParentDepartmentId" }]
        };
        RelationTreeMetadata.Validate(new() { Nodes = [node] }, ["department"]);
        var disconnected = node with { Key = "unreachable", RootWhenParentNull = false, Parents = [new() { NodeType = "unreachable", Field = "ParentDepartmentId" }] };
        Assert.Throws<JsonException>(() => RelationTreeMetadata.Validate(new() { Nodes = [node, disconnected] }, ["department", "unreachable"]));
    }

    [Fact]
    public void Definition_RejectsUnknownParentType()
    {
        var source = JsonSerializer.Deserialize<RelationTreeSource>("""
            {"nodes":[{"key":"department","model":"Department","resourceKey":"organization.department",
            "idField":"EID","labelField":"ENAME","codeField":"ECODE","sortField":"SortOrder",
            "parents":[{"nodeType":"missing","field":"ParentDepartmentId"}]}]}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Throws<JsonException>(() => RelationTreeMetadata.Validate(source, ["department"]));
    }

    [Fact]
    public void Definition_RejectsAmbiguousParentsAndExecutableFields()
    {
        var node = new RelationTreeNodeSource
        {
            Key = "department", Model = "Department", ResourceKey = "organization.department",
            IdField = "EID", LabelField = "ENAME", CodeField = "ECODE", SortField = "SortOrder",
            Parents = [new() { NodeType = "department", Field = "ParentDepartmentId" },
                new() { NodeType = "department", Field = "OtherParent" }]
        };
        Assert.Throws<JsonException>(() => RelationTreeMetadata.Validate(new() { Nodes = [node] }, ["department"]));
        Assert.Throws<JsonException>(() => RelationTreeMetadata.Validate(new() { Nodes = [node with
        { Parents = [], LabelField = "ENAME; DROP TABLE Department" }] }, ["department"]));
    }
}
