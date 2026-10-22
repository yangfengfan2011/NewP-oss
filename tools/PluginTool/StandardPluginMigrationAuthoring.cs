using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.PluginTool;

/// <summary>公开工具的静态尾节点 authoring；不装载插件代码、不连接数据库或执行 migration。</summary>
internal static class StandardPluginMigrationAuthoring
{
    /// <summary>核对外仓已发布前缀、实际 DLL 类型及模型，最后原子保存单个新尾节点。</summary>
    internal static async Task<string> FinalizeAsync(string root, StandardPluginBlueprint blueprint,
        string migrationId, string source, string compatibility, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        var bundle = blueprint.Persistence.Migrations[PlatformApiReleaseIdentity.SqlServerProvider];
        var manifestPath = Contained(root, bundle.Manifest);
        var assembly = await File.ReadAllBytesAsync(Contained(root, bundle.Assembly), token);
        var manifest = MigrationManifestIntegrity.Parse(await File.ReadAllBytesAsync(manifestPath, token));
        var prefix = MigrationManifestIntegrity.ValidateAuthoringPrefix(blueprint.ModuleId, PersistenceProvider.SqlServer, manifest, manifestPath);
        var existing = prefix.SingleOrDefault(step => step.MigrationId == migrationId);
        if (existing is not null && prefix[^1] != existing)
            throw Failure("MIGRATION_MANIFEST_NON_TAIL", "Only the tail migration can be finalized.");
        MigrationManifestIntegrity.ValidateMigrationTypes(prefix.Select(step => step.MigrationId).Append(migrationId).Distinct(StringComparer.Ordinal), ReadMigrationIds(assembly));
        var sourcePath = Contained(root, source);
        if (Path.GetFileName(sourcePath) != migrationId + ".cs")
            throw Failure("MIGRATION_MANIFEST_SOURCE_MISSING", "The canonical source filename must equal the migration ID.");
        var sourceRoot = Path.GetDirectoryName(sourcePath)!;
        foreach (var step in prefix)
        {
            var content = await File.ReadAllTextAsync(Path.Combine(sourceRoot, step.MigrationId + ".cs"), token);
            if (MigrationManifestIntegrity.ComputeCanonicalContentDigest(content) != step.ContentDigest)
                throw Failure("MIGRATION_MANIFEST_SOURCE_DRIFT", "Published migration source changed: " + step.MigrationId);
        }
        await ValidatePublishedPrefixAsync(root, manifestPath, blueprint.ModuleId, prefix, token);
        var loader = new JsonModelMetadataDocumentLoader();
        var projector = new ModelMetadataProjector();
        var validator = new ModelDefinitionValidator();
        var types = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(AppContext.BaseDirectory, "storage-types.json"));
        var models = blueprint.Persistence.Models.Order(StringComparer.Ordinal).Select(path =>
        {
            var virtualPath = Path.Combine(root, "modules", blueprint.ModuleId, path);
            var document = loader.Load(File.ReadAllBytes(Contained(root, path)), virtualPath);
            var model = projector.Project(document, types, "SqlServer") with
            {
                OwnerModuleId = blueprint.ModuleId,
                SchemaName = PersistenceStorageIdentity.ExternalSchema(blueprint.ModuleId)
            };
            validator.Validate(model);
            return model;
        }).ToArray();
        var digest = PersistenceModelDigest.Compute(models);
        var contentDigest = MigrationManifestIntegrity.ComputeCanonicalContentDigest(await File.ReadAllTextAsync(sourcePath, token));
        var previous = prefix.LastOrDefault();
        var tail = new MigrationManifestStep((prefix.Count + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
            migrationId, previous?.ContentDigest, contentDigest, previous?.ToModelDigest ?? new string('0', 64), digest, compatibility);
        if (existing is not null)
        {
            if (existing.Compatibility != compatibility || existing.ToModelDigest != digest || existing.ContentDigest != contentDigest)
                throw Failure("MIGRATION_MANIFEST_HISTORY_CHANGED", "A finalized tail cannot change its immutable facts.");
        }
        var finalized = manifest with
        {
            ModelDigest = digest,
            BundleChecksum = MigrationManifestIntegrity.ComputeBundleChecksum(assembly),
            Migrations = existing is null ? [.. prefix, tail] : prefix
        };
        MigrationManifestIntegrity.Validate(blueprint.ModuleId, PersistenceProvider.SqlServer, finalized, assembly, manifestPath, digest);
        var bytes = MigrationManifestIntegrity.SerializeCanonical(finalized);
        var original = await File.ReadAllBytesAsync(manifestPath, token);
        if (bytes.AsSpan().SequenceEqual(original)) return manifestPath;
        var temporary = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token);
            File.Move(temporary, manifestPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return manifestPath;
    }

    // PE metadata 只读取 MigrationAttribute 字符串，不解析 EF runtime 或执行外部类型初始化。
    private static IReadOnlyList<string> ReadMigrationIds(byte[] assembly)
    {
        using var stream = new MemoryStream(assembly, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var ids = new List<string>();
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            foreach (var handle in metadata.GetTypeDefinition(typeHandle).GetCustomAttributes())
            {
                var attribute = metadata.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var parent = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference) continue;
                var type = metadata.GetTypeReference((TypeReferenceHandle)parent);
                if (metadata.GetString(type.Namespace) != "Microsoft.EntityFrameworkCore.Migrations" || metadata.GetString(type.Name) != "MigrationAttribute") continue;
                var value = metadata.GetBlobReader(attribute.Value);
                if (value.ReadUInt16() != 1) throw new BadImageFormatException("Invalid migration attribute.");
                ids.Add(value.ReadSerializedString() ?? throw new BadImageFormatException("Missing migration ID."));
            }
        }
        return ids;
    }

    // HEAD 是外仓已发布前缀；索引和工作区可有新尾节点，但不能删除或改写 HEAD 中的历史。
    private static async Task ValidatePublishedPrefixAsync(string root, string path, string moduleId,
        IReadOnlyList<MigrationManifestStep> prefix, CancellationToken token)
    {
        var gitRoot = await GitAsync(root, ["rev-parse", "--show-toplevel"], token);
        if (gitRoot.ExitCode != 0) throw Failure("MIGRATION_MANIFEST_PUBLISHED_PREFIX_MISSING", "An authoring Git repository is required.");
        var relative = Path.GetRelativePath(gitRoot.Output.Trim(), path).Replace('\\', '/');
        var baseline = await GitAsync(root, ["show", "HEAD:" + relative], token);
        if (baseline.ExitCode != 0) throw Failure("MIGRATION_MANIFEST_PUBLISHED_PREFIX_MISSING", "Commit the initial empty manifest before finalizing its first tail.");
        var published = MigrationManifestIntegrity.Parse(System.Text.Encoding.UTF8.GetBytes(baseline.Output));
        var steps = MigrationManifestIntegrity.ValidateAuthoringPrefix(moduleId, PersistenceProvider.SqlServer, published, path);
        if (prefix.Count < steps.Count || !prefix.Take(steps.Count).SequenceEqual(steps))
            throw Failure("MIGRATION_MANIFEST_HISTORY_CHANGED", "The manifest changed its published prefix.");
    }

    // 显式参数调用 git，保留取消；只读取外仓 HEAD，不修改作者的提交或索引。
    private static async Task<(int ExitCode, string Output)> GitAsync(string root, string[] args, CancellationToken token)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Git could not start.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); await error; return (process.ExitCode, await output); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    // 输入路径只允许落在外仓内；依赖审计同时拒绝外部 reparse/source link。
    private static string Contained(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Migration authoring path must remain inside its root: " + relative);
        return path;
    }

    // 复用共享 manifest 异常形状，由 CLI 边界转换成可定位 diagnostic。
    private static MigrationManifestValidationException Failure(string code, string message) => new(code, message);
}
