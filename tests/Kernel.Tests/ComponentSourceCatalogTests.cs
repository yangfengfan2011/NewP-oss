using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class ComponentSourceCatalogTests
{
    [Fact]
    public void NestedBackendProjects_ResolveOwnerAndRejectAmbiguousCopies()
    {
        using var input = new RealComponentInput("part");
        var owner = Path.Combine(input.Root, "server", "modules", "Infor-Center", "Part");
        Directory.CreateDirectory(owner);
        var project = Path.Combine(owner, "PartModule.csproj");
        File.WriteAllText(project, "<Project><PropertyGroup><AssemblyName>PartModule</AssemblyName></PropertyGroup></Project>");
        var catalog = ComponentSourceCatalog.Discover(input.Root);
        Assert.Equal(project, catalog.ResolveProject(input.Root, "Part", "../../PartModule.dll"));
        var duplicate = Path.Combine(input.Root, "server", "modules", "Other", "Part");
        Directory.CreateDirectory(duplicate);
        File.Copy(project, Path.Combine(duplicate, "PartModule.csproj"));
        Assert.Throws<InvalidOperationException>(() => catalog.ResolveProject(input.Root, "Part", "../../PartModule.dll"));
    }

    [Theory]
    [InlineData("part")]
    [InlineData("members/part")]
    [InlineData("components/nested/part")]
    public void RealPartNestedAtAnyDepth_PreservesOwnerProjectAndIndependentAssets(string relative)
    {
        using var input = new RealComponentInput(relative);
        var catalog = ComponentSourceCatalog.Discover(input.Root);
        Assert.Equal(["InforCenter", "Part"], catalog.GetGroup("InforCenter").Select(component => component.ModuleId));
        Assert.Equal(input.PartRoot, catalog.Get("Part").Root);
        Assert.Equal("InforCenter", catalog.Get("Part").ParentModuleId);
        Assert.Equal(Path.Combine(input.Repository, "server", "modules", "Infor-Center", "Part", "PartModule.csproj"),
            catalog.ResolveProject(input.Repository, "Part", "../../PartModule.dll"));
        Assert.Equal(["newp.plugin.json"], catalog.EnumerateOwnFiles("InforCenter").Select(Path.GetFileName));
        Assert.Contains(catalog.EnumerateOwnFiles("Part"), path => path.EndsWith("Part.model.json", StringComparison.Ordinal));
        Assert.Contains(catalog.EnumerateOwnFiles("Part"), path => path.EndsWith("Part.SqlServerMigrations.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing", "MODULE_COMPOSITION_MEMBER_MISSING")]
    [InlineData("duplicate-member", "MODULE_COMPOSITION_MEMBER_DUPLICATE")]
    [InlineData("duplicate-id", "MODULE_IDENTITY_MISMATCH")]
    [InlineData("self", "MODULE_COMPOSITION_SELF_MEMBER")]
    [InlineData("cycle", "MODULE_COMPOSITION_CYCLE")]
    [InlineData("wrong-owner", "COMPONENT_DIRECTORY_MISMATCH")]
    [InlineData("outside-member", "COMPONENT_DIRECTORY_MISMATCH")]
    public void InvalidRealSourceSet_IsRejectedWithCapsuleLocation(string mutation, string code)
    {
        using var input = new RealComponentInput("part");
        var parent = JsonNode.Parse(File.ReadAllText(input.ParentCapsule))!;
        switch (mutation)
        {
            case "missing": Directory.Delete(input.PartRoot, true); break;
            case "duplicate-member": parent["composition"]!["members"] = new JsonArray("Part", "Part"); break;
            case "self": parent["composition"]!["members"] = new JsonArray("InforCenter"); break;
            case "wrong-owner": parent["composition"]!["members"] = new JsonArray(); break;
            case "duplicate-id": input.CopyCapsule("Part", Path.Combine(input.Root, "frontend", "modules", "duplicate")); break;
            case "outside-member":
                input.CopyCapsule("Document", Path.Combine(input.Root, "frontend", "modules", "document"));
                parent["composition"]!["members"] = new JsonArray("Part", "Document");
                break;
            case "cycle":
                var part = JsonNode.Parse(File.ReadAllText(input.PartCapsule))!;
                part["composition"] = new JsonObject { ["members"] = new JsonArray("InforCenter") };
                File.WriteAllText(input.PartCapsule, part.ToJsonString());
                break;
        }
        File.WriteAllText(input.ParentCapsule, parent.ToJsonString());
        var exception = Assert.Throws<InvalidOperationException>(() => ComponentSourceCatalog.Discover(input.Root));
        Assert.Contains(code, exception.Message);
        Assert.Contains("newp.plugin.json", exception.Message);
    }

    [Fact]
    public void FlatExistingComposition_IsDiscoverableButNotACompleteGroupedInput()
    {
        using var input = new RealComponentInput("part");
        var flat = Path.Combine(input.Root, "frontend", "modules", "part");
        Directory.Move(input.PartRoot, flat);
        var catalog = ComponentSourceCatalog.Discover(input.Root);
        Assert.Equal(flat, catalog.Get("Part").Root);
        var exception = Assert.Throws<InvalidOperationException>(() => catalog.GetGroup("InforCenter"));
        Assert.Contains("COMPONENT_DIRECTORY_MISMATCH", exception.Message);
    }

    private sealed class RealComponentInput : IDisposable
    {
        internal string Repository { get; } = FindRepository();
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "NewP-269-catalog-" + Guid.NewGuid().ToString("N"));
        internal string PartRoot { get; }
        internal string ParentCapsule => Path.Combine(Root, "frontend", "modules", "infor-center", "newp.plugin.json");
        internal string PartCapsule => Path.Combine(PartRoot, "newp.plugin.json");

        internal RealComponentInput(string relative)
        {
            PartRoot = Path.GetFullPath(Path.Combine(Root, "frontend", "modules", "infor-center", relative));
            CopyCapsule("InforCenter", Path.GetDirectoryName(ParentCapsule)!);
            CopyCapsule("Part", PartRoot);
            var parent = JsonNode.Parse(File.ReadAllText(ParentCapsule))!;
            parent["composition"]!["members"] = new JsonArray("Part");
            File.WriteAllText(ParentCapsule, parent.ToJsonString());
            foreach (var asset in new[] { "metadata/models/Part.model.json", "migrations/Part.SqlServerMigrations.json" })
            {
                var target = Path.Combine(PartRoot, asset);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(ComponentSourceCatalog.Discover(Repository).Get("Part").Root, asset), target);
            }
        }

        internal void CopyCapsule(string id, string target)
        {
            Directory.CreateDirectory(target);
            File.Copy(ComponentSourceCatalog.Discover(Repository).Get(id).CapsulePath, Path.Combine(target, "newp.plugin.json"));
        }

        public void Dispose() => Directory.Delete(Root, true);

        private static string FindRepository()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ICP-Platform.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("NewP repository not found.");
        }
    }
}
