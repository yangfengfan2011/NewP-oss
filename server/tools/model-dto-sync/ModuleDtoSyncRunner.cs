using ICP.Platform.Modeling.Dto;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Persistence.Composition;

namespace ICP.Platform.Tools.ModelDtoSync;

public static class ModuleDtoSyncRunner
{
    /// <summary>
    /// 从平台 catalog 读取当前模型投影，在仓库根下写入模块 Generated DTO 与签名 provider。
    /// </summary>
    public static IReadOnlyList<string> Run(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var generator = new ModelDtoGenerator();
        var codeWriter = new GeneratedCodeWriter();
        var signatureWriter = new DtoSignatureWriter();
        var writtenFiles = new List<string>();
        var signaturesByModule = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var metadataRepositoryRoot = ResolveMetadataRepositoryRoot(repositoryRoot);

        foreach (var definition in PlatformCoreLegacyModelSource.GetDefinitions().Where(IsDtoSyncModel))
        {
            var moduleId = ResolveModuleId(definition.Name);
            var outputDirectory = Path.Combine(repositoryRoot, "server", "modules", moduleId, "DTOs", "Generated");
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

        foreach (var module in signaturesByModule.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            var outputDirectory = Path.Combine(repositoryRoot, "server", "modules", module.Key, "DTOs", "Generated");
            writtenFiles.Add(signatureWriter.Write(outputDirectory, module.Key, module.Value));
        }

        return writtenFiles;
    }

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

    // 任务 5 只为已纳入 catalog 的内建模块建立受控映射，避免把模块归属推给元数据解释层。
    private static string ResolveModuleId(string modelName)
    {
        return modelName switch
        {
            "Role" or "Permission" or "RolePermission" or "UserRole" => "Authorization",
            "ConfigItem" or "ValueListItem" => "ConfigCenter",
            "Company" or "Department" or "Position" or "Person" or "PersonUserLink" => "Organization",
            _ => throw new InvalidOperationException($"模型 {modelName} 尚未配置 DTO 输出模块。")
        };
    }

    private static bool IsDtoSyncModel(ModelDefinition definition) =>
        definition.Name is
            "Role" or "Permission" or "RolePermission" or "UserRole"
            or "ConfigItem" or "ValueListItem"
            or "Company" or "Department" or "Position" or "Person" or "PersonUserLink";
}
