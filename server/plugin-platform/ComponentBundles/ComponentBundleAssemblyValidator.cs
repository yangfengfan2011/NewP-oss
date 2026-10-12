using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

// inspect 只读 PE/CLR metadata；绝不 Load、执行构造函数或实例化包内模块。
internal static class ComponentBundleAssemblyValidator
{
    // 校验真正被 runtime 使用的程序集身份、公共依赖版本与嵌入 Capsule。
    internal static void Validate(string path, IReadOnlyDictionary<string, Version> providedAssemblies,
        IReadOnlyDictionary<string, Version> frameworkAssemblies, JsonNode? authoring = null, bool requireRuntimeTarget = true)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) throw new BadImageFormatException("Managed metadata is missing.");
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly || metadata.GetString(metadata.GetAssemblyDefinition().Name) != Path.GetFileNameWithoutExtension(path))
                throw new BadImageFormatException("Assembly identity does not match declared asset.");
            var release = JsonNode.Parse(PlatformRuntimeReleaseIdentity.Descriptor)!;
            var frameworks = metadata.GetAssemblyDefinition().GetCustomAttributes().Select(metadata.GetCustomAttribute)
                .Where(attribute => IsAttribute(metadata, attribute, "System.Runtime.Versioning", "TargetFrameworkAttribute")).ToArray();
            if (frameworks.Length != 1) throw new BadImageFormatException("TargetFrameworkAttribute is missing or duplicated.");
            var frameworkReader = metadata.GetBlobReader(frameworks[0].Value);
            if (frameworkReader.ReadUInt16() != 1) throw new BadImageFormatException("Target framework metadata is malformed.");
            var framework = frameworkReader.ReadSerializedString();
            var expectedFramework = ".NETCoreApp,Version=v" + release["backend"]!["targetFramework"]!.GetValue<string>()[3..];
            if (requireRuntimeTarget && framework != expectedFramework)
                throw new InvalidDataException($"COMPONENT_BUNDLE_RUNTIME_MISMATCH: '{path}' targets '{framework}', expected '{expectedFramework}'.");
            var dependencies = release["backend"]!["assemblies"]!.AsObject();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                var name = metadata.GetString(reference.Name);
                if (dependencies[name] is JsonValue version && reference.Version.ToString() != version.GetValue<string>())
                    throw new InvalidDataException($"COMPONENT_BUNDLE_RUNTIME_MISMATCH: '{path}' references {name}@{reference.Version}.");
                if (dependencies.ContainsKey(name)) continue;
                if (!providedAssemblies.TryGetValue(name, out var available) && !frameworkAssemblies.TryGetValue(name, out available))
                    throw new InvalidDataException($"COMPONENT_BUNDLE_DEPENDENCY_MISSING: '{path}' references unprovided assembly '{name}'.");
                if (reference.Version != available)
                    throw new InvalidDataException($"COMPONENT_BUNDLE_RUNTIME_MISMATCH: '{path}' references {name}@{reference.Version}, provided '{available}'.");
            }
            if (authoring is null) return;
            var capsules = metadata.ManifestResources.Where(handle =>
                metadata.GetString(metadata.GetManifestResource(handle).Name).EndsWith(".newp.plugin.json", StringComparison.Ordinal)).ToArray();
            if (capsules.Length != 1) throw new BadImageFormatException("Assembly must contain exactly one Capsule.");
            var resource = metadata.GetManifestResource(capsules[0]);
            if (!resource.Implementation.IsNil) throw new BadImageFormatException("Capsule must be embedded in its owner assembly.");
            var reader = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress)
                .GetReader(checked((int)resource.Offset), pe.PEHeaders.CorHeader.ResourcesDirectory.Size - checked((int)resource.Offset));
            var capsule = JsonNode.Parse(reader.ReadBytes(reader.ReadInt32()));
            if (!JsonNode.DeepEquals(capsule, authoring))
                throw new InvalidDataException($"COMPONENT_BUNDLE_DECLARATION_MISMATCH: embedded Capsule in '{path}'.");
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException($"COMPONENT_BUNDLE_ASSEMBLY_INVALID: '{path}': {exception.Message}", exception);
        }
    }

    // 从已由 CLR 选择的共享框架文件读取身份；名称前缀不能证明某个程序集实际可用。
    internal static IReadOnlyDictionary<string, Version> FrameworkAssemblies()
    {
        var runtimeRoot = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var sharedRoot = Path.GetFullPath(Path.Combine(runtimeRoot, "..", "..")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => Path.GetFullPath(path).StartsWith(sharedRoot, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase);
        var identities = ReadIdentities(paths);
        var release = JsonNode.Parse(PlatformRuntimeReleaseIdentity.Descriptor)!;
        if (!identities.TryGetValue("System.Runtime", out var baseline) || baseline.ToString() != release["backend"]!["frameworkAssemblyVersion"]!.GetValue<string>())
            throw new InvalidDataException("COMPONENT_BUNDLE_RUNTIME_MISMATCH: actual framework release differs from controlled runtime.");
        return identities;
    }

    // 只读取受检资产的 CLR identity，不调用 Assembly.Load 或包内行为。
    internal static IReadOnlyDictionary<string, Version> ReadIdentities(IEnumerable<string> paths)
    {
        var result = new Dictionary<string, Version>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata) throw new BadImageFormatException("Managed metadata is missing.");
                var metadata = pe.GetMetadataReader();
                if (!metadata.IsAssembly) throw new BadImageFormatException("Assembly definition is missing.");
                var assembly = metadata.GetAssemblyDefinition();
                var name = metadata.GetString(assembly.Name);
                if (!result.TryAdd(name, assembly.Version))
                {
                    if (result[name] != assembly.Version || ComponentBundleIntegrity.HashFile(sources[name]) != ComponentBundleIntegrity.HashFile(path))
                        throw new InvalidDataException($"COMPONENT_BUNDLE_IDENTITY_MISMATCH: conflicting assembly '{name}'.");
                }
                else sources.Add(name, path);
            }
            catch (BadImageFormatException exception)
            {
                throw new InvalidDataException($"COMPONENT_BUNDLE_ASSEMBLY_INVALID: '{path}': {exception.Message}", exception);
            }
        }
        return result;
    }

    // 产物收集读取真实程序集引用闭包；模块依赖不能靠复制整个平台 bin 来满足。
    internal static IReadOnlyList<string> References(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
    }

    // 从 CLR attribute 构造器解析类型名称，无需反射加载任何包内类型。
    private static bool IsAttribute(MetadataReader metadata, CustomAttribute attribute, string namespaceName, string typeName)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference) return false;
        var parent = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference) return false;
        var type = metadata.GetTypeReference((TypeReferenceHandle)parent);
        return metadata.GetString(type.Namespace) == namespaceName && metadata.GetString(type.Name) == typeName;
    }
}
