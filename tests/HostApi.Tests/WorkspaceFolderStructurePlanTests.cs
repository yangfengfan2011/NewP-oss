using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Modules.Workspace;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceFolderStructurePlanTests
{
    [Fact]
    public void DiamondPreservesEveryEdgeAndOneFolderIdentity()
    {
        var plan = WorkspaceFolderStructurePlan.Create("S", [new("S", "root", "root description"), new("A", "A", null), new("B", "B", null), new("C", "C", "shared")],
            [new("SA", "S", "A", 20), new("SB", "S", "B", 50), new("AC", "A", "C", 40), new("BC", "B", "C", 10)]);
        Assert.Equal(new[] { "S", "A", "B", "C" }, plan.Folders.Select(folder => folder.Id));
        Assert.Equal(new[] { "SA", "SB", "AC", "BC" }, plan.Edges.Select(edge => edge.LinkId));
        Assert.Equal("shared", plan.Folders[^1].Description);
    }

    [Fact]
    public void RejectsCyclesDanglingEdgesDuplicateRelationsOrdinalsAndDisconnectedNodes()
    {
        WorkspaceStructureFolder[] nodes = [new("S", "S", null), new("A", "A", null), new("B", "B", null)];
        WorkspaceStructureEdge[][] invalid = [
            [new("SA", "S", "A", 1), new("AS", "A", "S", 1)],
            [new("SA", "S", "A", 1), new("AB", "A", "missing", 1)],
            [new("SA", "S", "A", 1), new("SA2", "S", "A", 2)],
            [new("SA", "S", "A", 1), new("SB", "S", "B", 1)],
            [new("SA", "S", "A", null)], [new("SA", "S", "A", -1)],
            [new("same", "S", "A", 1), new("same", "S", "B", 2)],
            [new("SA", "S", "A", 1)] ];
        foreach (var edges in invalid) Assert.Throws<LinkQueryException>(() => WorkspaceFolderStructurePlan.Create("S", nodes, edges));
        Assert.Throws<LinkQueryException>(() => WorkspaceFolderStructurePlan.Create("missing", nodes, []));
        Assert.Throws<LinkQueryException>(() => WorkspaceFolderStructurePlan.Create("S", [nodes[0], nodes[0]], []));
    }

    [Fact]
    public void SharedDagHandlesNodesAndEdgesInsteadOfItsTrillionPaths()
    {
        var nodes = new List<WorkspaceStructureFolder> { new("S", "S", null) }; var edges = new List<WorkspaceStructureEdge>(); string[] previous = ["S"];
        for (var depth = 0; depth < 20; depth++)
        {
            var current = Enumerable.Range(0, 4).Select(index => depth + "-" + index).ToArray();
            nodes.AddRange(current.Select(id => new WorkspaceStructureFolder(id, id, null)));
            foreach (var parent in previous) for (var index = 0; index < current.Length; index++) edges.Add(new(parent + ":" + current[index], parent, current[index], index + 1));
            previous = current;
        }
        var plan = WorkspaceFolderStructurePlan.Create("S", nodes, edges);
        Assert.Equal(81, plan.Folders.Count); Assert.Equal(308, plan.Edges.Count);
    }
}
