using System.Text.Json;
using System.Xml.Linq;
using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

/// <summary>
/// 审计仓外 consumer 的声明式依赖，拒绝任何源码或 workspace 绑定。
/// </summary>
public sealed class ExternalConsumerDependencyAuditor
{
    private static readonly string[] DependencySections =
    [
        "dependencies",
        "devDependencies",
        "peerDependencies",
        "optionalDependencies"
    ];

    private static readonly string[] ForbiddenDotNetPackagePrefixes =
    [
        "ICP.Platform.Kernel",
        "ICP.Platform.Host",
        "ICP.Platform.Persistence",
        "ICP.Platform.Modules.",
        "ICP.Platform.Sdk"
    ];

    private static readonly HashSet<string> ForbiddenFrontendPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "@icp-platform/core",
        "@icp-platform/ui",
        "@icp-platform/sdk"
    };

    public IReadOnlyList<PlatformApiDiagnostic> Audit(string consumerRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerRoot);
        var root = Path.GetFullPath(consumerRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Consumer root does not exist: {root}");
        }

        var diagnostics = new List<PlatformApiDiagnostic>();
        var files = EnumerateOwnedSourceFiles(root, diagnostics);
        foreach (var projectPath in files.Where(static path =>
                     string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            AuditProject(projectPath, diagnostics);
        }

        foreach (var packageJsonPath in files.Where(static path =>
                     string.Equals(Path.GetFileName(path), "package.json", StringComparison.OrdinalIgnoreCase)))
        {
            AuditPackageJson(packageJsonPath, diagnostics);
        }

        return diagnostics
            .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> EnumerateOwnedSourceFiles(
        string root,
        List<PlatformApiDiagnostic> diagnostics)
    {
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var directories = new Stack<string>();
        var files = new List<string>();
        directories.Push(root);

        while (directories.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    AuditLink(rootPrefix, entry, attributes, diagnostics);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    files.Add(entry);
                    continue;
                }

                if (Path.GetFileName(entry) is ".git" or "node_modules" or "bin" or "obj" or ".artifacts")
                {
                    continue;
                }

                directories.Push(entry);
            }
        }

        return files;
    }

    private static void AuditLink(
        string rootPrefix,
        string path,
        FileAttributes attributes,
        List<PlatformApiDiagnostic> diagnostics)
    {
        FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        var resolvedTarget = target is null ? null : Path.GetFullPath(target.FullName);
        if (resolvedTarget is not null
            && resolvedTarget.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        diagnostics.Add(new PlatformApiDiagnostic(
            PlatformApiDiagnosticCodes.SourcePathDependencyForbidden,
            "dependency-audit",
            "External consumers cannot read authoring content through a source link outside their owned root.",
            SourcePath: path,
            ResolvedPackageVersion: resolvedTarget));
    }

    private static void AuditProject(string projectPath, List<PlatformApiDiagnostic> diagnostics)
    {
        var document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
        foreach (var reference in document.Descendants().Where(static element => element.Name.LocalName == "ProjectReference"))
        {
            diagnostics.Add(new PlatformApiDiagnostic(
                PlatformApiDiagnosticCodes.ProjectReferenceForbidden,
                "dependency-audit",
                "External consumers must restore immutable packages and cannot use ProjectReference.",
                SourcePath: projectPath,
                JsonPointer: reference.Attribute("Include")?.Value));
        }

        foreach (var reference in document.Descendants().Where(static element => element.Name.LocalName == "PackageReference"))
        {
            var packageId = reference.Attribute("Include")?.Value
                ?? reference.Attribute("Update")?.Value
                ?? string.Empty;
            if (!ForbiddenDotNetPackagePrefixes.Any(prefix =>
                    packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            diagnostics.Add(new PlatformApiDiagnostic(
                PlatformApiDiagnosticCodes.ForbiddenPlatformReference,
                "dependency-audit",
                $"Dependency '{packageId}' is a platform runtime implementation and is not part of standard-data.v1.",
                SourcePath: projectPath,
                JsonPointer: reference.Attribute("Include")?.Value,
                ResolvedPackageId: packageId,
                ResolvedPackageVersion: reference.Attribute("Version")?.Value
                    ?? reference.Elements().FirstOrDefault(static element => element.Name.LocalName == "Version")?.Value));
        }
    }

    private static void AuditPackageJson(string packageJsonPath, List<PlatformApiDiagnostic> diagnostics)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        foreach (var sectionName in DependencySections)
        {
            if (!document.RootElement.TryGetProperty(sectionName, out var section)
                || section.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var dependency in section.EnumerateObject())
            {
                if (dependency.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var version = dependency.Value.GetString() ?? string.Empty;
                if (ForbiddenFrontendPackages.Contains(dependency.Name))
                {
                    diagnostics.Add(new PlatformApiDiagnostic(
                        PlatformApiDiagnosticCodes.ForbiddenPlatformReference,
                        "dependency-audit",
                        $"Dependency '{dependency.Name}' is a platform runtime package and is not part of standard-data.v1.",
                        SourcePath: packageJsonPath,
                        JsonPointer: $"/{sectionName}/{dependency.Name}",
                        ResolvedPackageId: dependency.Name,
                        ResolvedPackageVersion: version));
                }

                if (version.StartsWith("workspace:", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new PlatformApiDiagnostic(
                        PlatformApiDiagnosticCodes.WorkspaceDependencyForbidden,
                        "dependency-audit",
                        $"Dependency '{dependency.Name}' cannot use a workspace range.",
                        SourcePath: packageJsonPath,
                        JsonPointer: $"/{sectionName}/{dependency.Name}",
                        ResolvedPackageId: dependency.Name,
                        ResolvedPackageVersion: version));
                }
                else if (version.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                         || version.StartsWith("link:", StringComparison.OrdinalIgnoreCase)
                         || version.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new PlatformApiDiagnostic(
                        PlatformApiDiagnosticCodes.SourcePathDependencyForbidden,
                        "dependency-audit",
                        $"Dependency '{dependency.Name}' cannot resolve from a source path.",
                        SourcePath: packageJsonPath,
                        JsonPointer: $"/{sectionName}/{dependency.Name}",
                        ResolvedPackageId: dependency.Name,
                        ResolvedPackageVersion: version));
                }
            }
        }
    }
}
