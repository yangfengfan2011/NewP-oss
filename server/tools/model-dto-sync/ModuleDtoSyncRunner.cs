using ICP.Platform.Modeling.Dto;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Tools.ModelDtoSync;

public static class ModuleDtoSyncRunner
{
    /// <summary>
    /// 从 modules/*/metadata/models 读取当前模型投影，在仓库根下写入模块 Generated DTO 与签名 provider。
    /// </summary>
    public static IReadOnlyList<string> Run(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        return Generate(repositoryRoot, ResolveMetadataRepositoryRoot(repositoryRoot));
    }

    /// <summary>
    /// 比较当前 Generated DTO 与模型投影，不修改仓库，并返回发生漂移的仓库相对路径。
    /// </summary>
    public static IReadOnlyList<string> Check(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "newp-model-dto-sync-check",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var expectedFiles = Generate(temporaryRoot, ResolveMetadataRepositoryRoot(repositoryRoot));
            var expectedByRelativePath = expectedFiles.ToDictionary(
                path => NormalizeRelativePath(Path.GetRelativePath(temporaryRoot, path)),
                static path => path,
                StringComparer.Ordinal);
            var actualByRelativePath = EnumerateManagedGeneratedFiles(repositoryRoot).ToDictionary(
                path => NormalizeRelativePath(Path.GetRelativePath(repositoryRoot, path)),
                static path => path,
                StringComparer.Ordinal);

            return expectedByRelativePath.Keys
                .Union(actualByRelativePath.Keys, StringComparer.Ordinal)
                .Where(relativePath =>
                    !expectedByRelativePath.TryGetValue(relativePath, out var expectedPath)
                    || !actualByRelativePath.TryGetValue(relativePath, out var actualPath)
                    || !File.ReadAllBytes(expectedPath).SequenceEqual(File.ReadAllBytes(actualPath)))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static IReadOnlyList<string> Generate(string outputRepositoryRoot, string metadataRepositoryRoot)
    {

        var generator = new ModelDtoGenerator();
        var codeWriter = new GeneratedCodeWriter();
        var signatureWriter = new DtoSignatureWriter();
        var writtenFiles = new List<string>();
        var signaturesByModule = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        var modelLoader = new JsonModelMetadataDocumentLoader();
        var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(
            metadataRepositoryRoot,
            "server",
            "platform-modeling",
            "metadata",
            "storage-types.json"));
        var validator = new ModelMetadataValidator();
        var projector = new ModelMetadataProjector();
        var modulesRoot = Path.Combine(metadataRepositoryRoot, "modules");

        foreach (var moduleRoot in Directory.EnumerateDirectories(modulesRoot)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var moduleId = Path.GetFileName(moduleRoot);
            var modelDirectory = Path.Combine(moduleRoot, "metadata", "models");
            if (!Directory.Exists(modelDirectory))
            {
                continue;
            }

            foreach (var modelPath in Directory.EnumerateFiles(modelDirectory, "*.model.json")
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                var document = modelLoader.LoadFromFile(modelPath);
                validator.Validate(document, typeMap);
                var definition = projector.Project(document, typeMap, "SqlServer");
                var outputDirectory = Path.Combine(outputRepositoryRoot, "server", "modules", moduleId, "DTOs", "Generated");
                var outputPath = Path.Combine(outputDirectory, definition.Name + "Dto.g.cs");
                var enumClrTypeMap = LoadEnumClrTypeMap(metadataRepositoryRoot, moduleId, definition.Name);
                var sourceCode = GeneratedCodeWriter.WrapInNamespace(
                    generator.Generate(definition, enumClrTypeMap),
                    $"ICP.Platform.Modules.{moduleId}.DTOs",
                    ResolveUsingNamespaces(moduleId, enumClrTypeMap));

                var writtenSourceCode = codeWriter.Write(outputPath, sourceCode);
                writtenFiles.Add(outputPath);

                if (!signaturesByModule.TryGetValue(moduleId, out var moduleSignatures))
                {
                    moduleSignatures = new Dictionary<string, string>(StringComparer.Ordinal);
                    signaturesByModule[moduleId] = moduleSignatures;
                }

                moduleSignatures[definition.Name + "Dto"] = DtoSignatureWriter.ComputeSignature(writtenSourceCode);
            }
        }

        foreach (var module in signaturesByModule.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            var outputDirectory = Path.Combine(outputRepositoryRoot, "server", "modules", module.Key, "DTOs", "Generated");
            writtenFiles.Add(signatureWriter.Write(outputDirectory, module.Key, module.Value));
        }

        return writtenFiles;
    }

    private static IEnumerable<string> EnumerateManagedGeneratedFiles(string repositoryRoot)
    {
        var modulesRoot = Path.Combine(repositoryRoot, "server", "modules");
        if (!Directory.Exists(modulesRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(modulesRoot)
            .Select(moduleRoot => Path.Combine(moduleRoot, "DTOs", "Generated"))
            .Where(Directory.Exists)
            .SelectMany(generatedRoot => Directory.EnumerateFiles(generatedRoot, "*.g.cs", SearchOption.TopDirectoryOnly));
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    // DTO 生成必须回读 authoring 元数据中的 contract.clrType，避免 enum 字段退化为 string。
    private static IReadOnlyDictionary<string, string> LoadEnumClrTypeMap(
        string repositoryRoot,
        string moduleId,
        string modelName)
    {
        var modelPath = Path.Combine(
            repositoryRoot,
            "modules",
            moduleId,
            "metadata",
            "models",
            modelName + ".model.json");

        if (!File.Exists(modelPath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var document = new JsonModelMetadataDocumentLoader().LoadFromFile(modelPath);
        return document.Model.Fields
            .Select(static field => new { field.Name, field.Contract })
            .Where(static field =>
                string.Equals(field.Contract?.ValueType, "enum", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(field.Contract?.ClrType))
            .ToDictionary(
                static field => field.Name,
                static field => field.Contract!.ClrType!,
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ResolveUsingNamespaces(
        string moduleId,
        IReadOnlyDictionary<string, string> enumClrTypeMap)
    {
        return enumClrTypeMap.Count == 0
            ? Array.Empty<string>()
            : new[] { $"ICP.Platform.Modules.{moduleId}.Models" };
    }

    private static string ResolveMetadataRepositoryRoot(string outputRepositoryRoot)
    {
        if (Directory.Exists(Path.Combine(outputRepositoryRoot, "modules")))
        {
            return Path.GetFullPath(outputRepositoryRoot);
        }

        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "modules"))
                    && Directory.Exists(Path.Combine(current.FullName, "server")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("无法定位模型元数据所在的仓库根目录。");
    }

}
