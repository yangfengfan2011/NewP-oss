using ICP.Platform.Modeling.Dto;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Tools.ModelDtoSync;

public static class ModuleDtoSyncRunner
{
    /// <summary>
    /// 从 frontend/modules/*/metadata/models 读取当前模型投影，在仓库根下写入模块 Generated DTO 与签名 provider。
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
                    || !ContentsEqualIgnoringLineEndings(expectedPath, actualPath))
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

    // 每个模块先形成完整 model catalog，再让 DTO 只消费统一 planner 的成功编译结果。
    private static IReadOnlyList<string> Generate(string outputRepositoryRoot, string metadataRepositoryRoot)
    {
        var generator = new ModelDtoGenerator();
        IModelDisplayProjectionPlanner displayPlanner = new ModelDisplayProjectionPlanner();
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
        var modulesRoot = Path.Combine(metadataRepositoryRoot, "frontend", "modules");

        foreach (var moduleRoot in Directory.EnumerateDirectories(modulesRoot)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var moduleId = ModuleAuthoringPaths.ReadModuleId(moduleRoot);
            var modelDirectory = Path.Combine(moduleRoot, "metadata", "models");
            if (!Directory.Exists(modelDirectory))
            {
                continue;
            }

            var modelCandidates = new List<ModelDefinition>();
            foreach (var modelPath in Directory.EnumerateFiles(modelDirectory, "*.model.json")
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                var document = modelLoader.LoadFromFile(modelPath);
                validator.Validate(document, typeMap);
                var definition = projector.Project(document, typeMap, "SqlServer");
                modelCandidates.Add(definition);
            }

            var modelCatalog = modelCandidates.ToArray();
            foreach (var definition in modelCatalog)
            {
                var displayCompilation = displayPlanner.Compile(definition, modelCatalog);
                if (!displayCompilation.IsSuccess)
                {
                    throw new InvalidOperationException(string.Join(
                        Environment.NewLine,
                        displayCompilation.Diagnostics.Select(static diagnostic =>
                            $"{diagnostic.Code}: {diagnostic.ModelName}.{diagnostic.FieldName}: {diagnostic.Message}")));
                }

                var outputDirectory = Path.Combine(outputRepositoryRoot, "server", "modules", moduleId, "DTOs", "Generated");
                var outputPath = Path.Combine(outputDirectory, definition.Name + "Dto.g.cs");
                var enumClrTypeMap = LoadEnumClrTypeMap(moduleRoot, definition.Name);
                var sourceCode = GeneratedCodeWriter.WrapInNamespace(
                    generator.Generate(definition, displayCompilation.Plan!, enumClrTypeMap),
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

    // Git checkout 可按本机策略把已跟踪 LF 文件物化为 CRLF；漂移检查只忽略这一物理换行差异。
    private static bool ContentsEqualIgnoringLineEndings(string expectedPath, string actualPath)
    {
        return NormalizeLineEndings(File.ReadAllBytes(expectedPath))
            .SequenceEqual(NormalizeLineEndings(File.ReadAllBytes(actualPath)));
    }

    // 在字节层规范化 CRLF/CR，保留 BOM、编码与其余内容差异的 fail-closed 行为。
    private static byte[] NormalizeLineEndings(byte[] content)
    {
        using var normalized = new MemoryStream(content.Length);
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != '\r')
            {
                normalized.WriteByte(content[index]);
                continue;
            }

            if (index + 1 < content.Length && content[index + 1] == '\n')
            {
                index++;
            }

            normalized.WriteByte((byte)'\n');
        }

        return normalized.ToArray();
    }

    // DTO 生成必须回读 authoring 元数据中的 contract.clrType，避免 enum 字段退化为 string。
    // enum CLR 类型来自同一次选择的 authoring 模型，避免用 slug 重推身份。
    private static IReadOnlyDictionary<string, string> LoadEnumClrTypeMap(
        string moduleRoot,
        string modelName)
    {
        var modelPath = Path.Combine(
            moduleRoot,
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

    // 临时输出目录没有 authoring 时回到实际源码根。
    private static string ResolveMetadataRepositoryRoot(string outputRepositoryRoot)
    {
        if (Directory.Exists(Path.Combine(outputRepositoryRoot, "frontend", "modules")))
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
                if (Directory.Exists(Path.Combine(current.FullName, "frontend", "modules"))
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
