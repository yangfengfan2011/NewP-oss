using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 内建模块签发时冻结的工件字节；消费方只读取该快照，不重新打开 authoring 路径。
/// </summary>
internal sealed class VerifiedModuleArtifactSnapshot
{
    private readonly IReadOnlyDictionary<string, byte[]> _moduleFiles;
    private readonly IReadOnlyDictionary<string, byte[]> _distributionFiles;

    private VerifiedModuleArtifactSnapshot(
        string moduleRoot,
        IReadOnlyDictionary<string, byte[]> moduleFiles,
        IReadOnlyDictionary<string, byte[]> distributionFiles)
    {
        ModuleRoot = Path.GetFullPath(moduleRoot);
        _moduleFiles = moduleFiles;
        _distributionFiles = distributionFiles;
        ContentDigest = ComputeContentDigest(moduleFiles, distributionFiles);
    }

    internal string ModuleRoot { get; }

    internal string ContentDigest { get; }

    internal static VerifiedModuleArtifactSnapshot Capture(
        string moduleRoot,
        Action<string>? beforeOpen = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(moduleRoot));
        var paths = EnumeratePhysicalFiles(root);
        var opened = new List<(string RelativePath, VerifiedOpenFile File)>(paths.Count);
        try
        {
            foreach (var relativePath in paths)
            {
                beforeOpen?.Invoke(relativePath);
                opened.Add((
                    relativePath,
                    PathContainment.OpenContainedFile(root, Path.Combine(root, relativePath))));
            }

            var verifiedPaths = EnumeratePhysicalFiles(root);
            if (!paths.SequenceEqual(verifiedPaths, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Built-in module tree changed while its artifact snapshot was being captured: {root}");
            }

            var files = opened.ToDictionary(
                static item => NormalizeRelativePath(item.RelativePath),
                static item => item.File.ReadAllBytes(),
                StringComparer.OrdinalIgnoreCase);
            return new VerifiedModuleArtifactSnapshot(
                root,
                new ReadOnlyDictionary<string, byte[]>(files),
                new ReadOnlyDictionary<string, byte[]>(
                    new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)));
        }
        finally
        {
            foreach (var item in opened)
            {
                item.File.Dispose();
            }
        }
    }

    internal VerifiedModuleArtifactSnapshot WithDistributionFile(string declaredPath, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredPath);
        ArgumentNullException.ThrowIfNull(bytes);
        var distributionFiles = _distributionFiles.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        distributionFiles[NormalizeDeclaredPath(declaredPath)] = bytes.ToArray();
        return new VerifiedModuleArtifactSnapshot(
            ModuleRoot,
            _moduleFiles,
            new ReadOnlyDictionary<string, byte[]>(distributionFiles));
    }

    internal bool ContainsFile(string relativePath) =>
        _moduleFiles.ContainsKey(NormalizeRelativePath(relativePath));

    internal byte[] ReadFile(string relativePath)
    {
        var key = NormalizeRelativePath(relativePath);
        return _moduleFiles.TryGetValue(key, out var bytes)
            ? bytes.ToArray()
            : throw new FileNotFoundException(
                $"Verified built-in artifact is absent from the signed snapshot: {relativePath}",
                Path.Combine(ModuleRoot, relativePath));
    }

    internal string ReadText(string relativePath) => Encoding.UTF8.GetString(ReadFile(relativePath));

    internal byte[] ReadDistributionFile(string declaredPath)
    {
        var key = NormalizeDeclaredPath(declaredPath);
        return _distributionFiles.TryGetValue(key, out var bytes)
            ? bytes.ToArray()
            : throw new FileNotFoundException(
                $"Verified built-in distribution artifact is absent from the signed snapshot: {declaredPath}",
                Path.Combine(ModuleRoot, declaredPath));
    }

    internal IReadOnlyList<string> EnumerateFiles(string relativeRoot)
    {
        var prefix = NormalizeRelativePath(relativeRoot).TrimEnd('/') + "/";
        return _moduleFiles.Keys
            .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> EnumeratePhysicalFiles(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(root, path)))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidOperationException($"Module artifact path must be package-relative: {path}");
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new InvalidOperationException($"Module artifact path escapes its snapshot: {path}");
        }

        return string.Join('/', segments);
    }

    private static string NormalizeDeclaredPath(string path) =>
        path.Replace('\\', '/');

    private static string ComputeContentDigest(
        IReadOnlyDictionary<string, byte[]> moduleFiles,
        IReadOnlyDictionary<string, byte[]> distributionFiles)
    {
        var facts = moduleFiles
            .Select(static pair => new
            {
                scope = "module",
                path = pair.Key,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(pair.Value))
            })
            .Concat(distributionFiles.Select(static pair => new
            {
                scope = "distribution",
                path = pair.Key,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(pair.Value))
            }))
            .OrderBy(static fact => fact.scope, StringComparer.Ordinal)
            .ThenBy(static fact => fact.path, StringComparer.Ordinal)
            .ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(facts)));
    }
}
