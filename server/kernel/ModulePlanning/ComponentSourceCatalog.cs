using System.Text.Json;
using System.Xml.Linq;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>按 Capsule 身份关联可移动源码根、原位后端工程及独立资产边界。</summary>
public sealed class ComponentSourceCatalog
{
    private static readonly HashSet<string> GeneratedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", "dist", "bin", "obj", ".git", ".vite", "coverage" };
    private readonly IReadOnlyList<ComponentSourceRoot> _components;

    // 每次发现使用当前 authoring，不缓存路径或另建人工维护的成员表。
    private ComponentSourceCatalog(IReadOnlyList<ComponentSourceRoot> components) => _components = components;

    public IReadOnlyList<ComponentSourceRoot> Components => _components;

    /// <summary>发现整个仓库并复用 Canonical 编译器校验完整身份与组合图。</summary>
    public static ComponentSourceCatalog Discover(string repositoryRoot)
    {
        var sourcesRoot = Path.Combine(Path.GetFullPath(repositoryRoot), "frontend", "modules");
        if (!Directory.Exists(sourcesRoot)) throw new DirectoryNotFoundException($"Component source root not found: {sourcesRoot}");
        var components = new List<ComponentSourceRoot>();
        foreach (var directory in Directory.EnumerateDirectories(sourcesRoot).Order(StringComparer.Ordinal))
            DiscoverDirectory(directory, null, components);
        var catalog = new ComponentSourceCatalog(components.OrderBy(component => component.ModuleId, StringComparer.Ordinal).ToArray());
        catalog.ValidateComposition();
        return catalog;
    }

    /// <summary>按声明身份定位组件；目录 slug 与嵌套深度不参与身份比较。</summary>
    public ComponentSourceRoot Get(string moduleId) => _components.SingleOrDefault(component => component.ModuleId == moduleId)
        ?? throw new InvalidOperationException($"COMPONENT_SOURCE_MISSING: module='{moduleId}' has no authoring Capsule.");

    /// <summary>校验以某个组件为输入的完整归组集合，拒绝声明成员落在集合之外。</summary>
    public IReadOnlyList<ComponentSourceRoot> GetGroup(string moduleId)
    {
        var result = new List<ComponentSourceRoot>();
        var pending = new Stack<ComponentSourceRoot>();
        pending.Push(Get(moduleId));
        while (pending.TryPop(out var current))
        {
            ValidateDirectoryMembers(current);
            result.Add(current);
            foreach (var member in current.Members.Reverse()) pending.Push(Get(member));
        }
        return result;
    }

    /// <summary>枚举组件自有文件；遇到其他组件根即停止，不吞并独立成员资产。</summary>
    public IEnumerable<string> EnumerateOwnFiles(string moduleId)
    {
        var root = Get(moduleId).Root;
        var boundaries = _components.Where(component => component.Root != root).Select(component => component.Root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory).OrderDescending(StringComparer.Ordinal))
                if (!boundaries.Contains(child) && !GeneratedDirectories.Contains(Path.GetFileName(child))) pending.Push(child);
        }
    }

    /// <summary>从声明的程序集名匹配原位 owner 工程；缺失或多重匹配直接拒绝。</summary>
    public string ResolveProject(string repositoryRoot, string moduleId, string assemblyPath)
    {
        var ownerRoot = ResolveBackendRoot(repositoryRoot, moduleId);
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        var matches = Directory.Exists(ownerRoot)
            ? Directory.EnumerateFiles(ownerRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(ownerRoot, path).Split(Path.DirectorySeparatorChar).Any(GeneratedDirectories.Contains))
                .Where(path => (ReadProjectProperty(path, "AssemblyName") ?? Path.GetFileNameWithoutExtension(path)) == assemblyName).ToArray()
            : [];
        if (matches.Length != 1)
            throw new InvalidOperationException($"COMPONENT_PROJECT_MISMATCH: module='{moduleId}', capsule='{Get(moduleId).CapsulePath}', assembly='{assemblyPath}', owner='{ownerRoot}': expected one project, found {matches.Length}.");
        return matches[0];
    }

    /// <summary>按独立工程所在的 owner 目录定位后端，不把分组目录写进组件身份。</summary>
    public static string ResolveBackendRoot(string repositoryRoot, string moduleId)
    {
        var roots = EnumerateSourceDirectories(Path.Combine(Path.GetFullPath(repositoryRoot), "server", "modules"))
            .Where(path => Path.GetFileName(path) == moduleId && Directory.EnumerateFiles(path, "*.csproj").Any()).ToArray();
        if (roots.Length != 1)
            throw new InvalidOperationException($"COMPONENT_PROJECT_MISMATCH: module='{moduleId}' expected one backend owner root, found {roots.Length}.");
        return roots[0];
    }

    /// <summary>枚举源码目录，跳过构建产物与链接，支持任意分组深度。</summary>
    public static IEnumerable<string> EnumerateSourceDirectories(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            yield return directory;
            foreach (var child in Directory.EnumerateDirectories(directory).OrderDescending(StringComparer.Ordinal))
                if (!GeneratedDirectories.Contains(Path.GetFileName(child)) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    pending.Push(child);
        }
    }

    /// <summary>历史 metadata 与前端入口按原 slug 定位；普通分组目录不授予组件身份。</summary>
    public static string ResolveLegacyFrontendRoot(string repositoryRoot, string slug)
    {
        var root = Path.Combine(Path.GetFullPath(repositoryRoot), "frontend", "modules");
        var matches = EnumerateSourceDirectories(root).Where(path => Path.GetFileName(path) == slug
            && (Directory.Exists(Path.Combine(path, "metadata")) || File.Exists(Path.Combine(path, "src", "index.ts")))).ToArray();
        return matches.Length switch
        {
            0 => Path.Combine(root, slug),
            1 => matches[0],
            _ => throw new InvalidOperationException($"COMPONENT_SOURCE_AMBIGUOUS: legacy slug='{slug}' has multiple roots.")
        };
    }

    /// <summary>读取实际工程属性，避免从源码目录层级推断产物名或目标框架。</summary>
    public static string? ReadProjectProperty(string projectPath, string name) => XDocument.Load(projectPath).Descendants()
        .FirstOrDefault(element => element.Name.LocalName == name)?.Value;

    // 仅 Capsule 授予组件身份；无 Capsule 的历史 metadata 根由旧消费者自行恢复 owner。
    private static void DiscoverDirectory(string directory, string? parentId, List<ComponentSourceRoot> components)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"COMPONENT_SOURCE_PATH_INVALID: cannot traverse reparse point '{directory}'.");
        var capsulePath = Path.Combine(directory, "newp.plugin.json");
        if (File.Exists(capsulePath))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(capsulePath));
            var capsule = document.RootElement.Clone();
            var moduleId = RequiredString(capsule, "moduleId", capsulePath);
            var version = RequiredString(capsule, "version", capsulePath);
            var members = Array.Empty<string>();
            if (capsule.TryGetProperty("composition", out var composition))
            {
                if (composition.ValueKind != JsonValueKind.Object || !composition.TryGetProperty("members", out var array) || array.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException($"MODULE_COMPOSITION_INVALID: '{capsulePath}' /composition/members must be an array.");
                members = array.EnumerateArray().Select(member => member.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(member.GetString())
                    ? member.GetString()! : throw new InvalidOperationException($"MODULE_COMPOSITION_INVALID: '{capsulePath}' /composition/members contains an invalid id.")).ToArray();
            }
            components.Add(new(moduleId, version, directory, capsulePath, parentId, capsule, members));
            parentId = moduleId;
        }
        foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            if (!GeneratedDirectories.Contains(Path.GetFileName(child))) DiscoverDirectory(child, parentId, components);
    }

    // 图规则仍由正式编译器拥有；源码发现只增加声明与最近组件目录的对应约束。
    private void ValidateComposition()
    {
        var candidates = _components.Select(component => new ModuleCapsuleCandidate(component.ModuleId, component.Version,
            component.ModuleId, null, component.Root, null, null, null, null, "behavior-bindings", [], [], [], [], [],
            new ModulePlanProvenance("authoring", component.CapsulePath, "/")) { CompositionMembers = component.Members, Enabled = false });
        var compilation = new CanonicalModulePlanCompiler().Compile("component-sources", candidates);
        if (!compilation.IsSuccess)
            throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: '{diagnostic.SourcePath}' {diagnostic.JsonPointer}: {diagnostic.Message}")));
        foreach (var component in _components.Where(component => component.ParentModuleId is not null))
        {
            var parent = Get(component.ParentModuleId!);
            if (!parent.Members.Contains(component.ModuleId, StringComparer.Ordinal))
                throw new InvalidOperationException($"COMPONENT_DIRECTORY_MISMATCH: '{component.CapsulePath}' belongs physically to '{parent.ModuleId}' but is absent from '{parent.CapsulePath}' /composition/members.");
        }
        // 既有平面组合保持可用；开始实际嵌套后，该父组件的全部直属成员都必须在它的源码边界内。
        foreach (var parent in _components.Where(parent => _components.Any(child => child.ParentModuleId == parent.ModuleId)))
            ValidateDirectoryMembers(parent);
    }

    // 使用最近 Capsule 根识别直属成员，目录名称和中间分组目录均不成为协议。
    private void ValidateDirectoryMembers(ComponentSourceRoot parent)
    {
        foreach (var memberId in parent.Members)
        {
            var member = Get(memberId);
            if (member.ParentModuleId != parent.ModuleId)
                throw new InvalidOperationException($"COMPONENT_DIRECTORY_MISMATCH: '{parent.CapsulePath}' /composition/members declares '{memberId}', but '{member.CapsulePath}' is outside its source root.");
        }
    }

    // 非空且安全的身份才能用于 owner 工程路径，不允许声明借路径穿越改变 owner。
    private static string RequiredString(JsonElement capsule, string name, string path)
    {
        if (!capsule.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"COMPONENT_IDENTITY_INVALID: '{path}' /{name} is required.");
        var result = value.GetString()!;
        if (name == "moduleId" && (result.IndexOfAny(['/', '\\']) >= 0 || result is "." or ".."))
            throw new InvalidOperationException($"COMPONENT_IDENTITY_INVALID: '{path}' /{name} cannot contain a path.");
        return result;
    }
}

/// <summary>一次发现的组件 authoring 事实；父关系来自 JSON，ParentModuleId 只用于核对目录。</summary>
public sealed record ComponentSourceRoot(string ModuleId, string Version, string Root, string CapsulePath,
    string? ParentModuleId, JsonElement Capsule, IReadOnlyList<string> Members);
