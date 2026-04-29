using System.Text.RegularExpressions;
using ICP.Platform.Modeling.Dto;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Persistence.DevSync.Rendering;

namespace ICP.Platform.Persistence.DevSync.Coordination;

/// <summary>
/// 表示基础 DTO 签名校验结果；Host 可用 <see cref="Mismatches"/> 作为阻断启动时的失败信号。
/// </summary>
public sealed record BaseDtoSignatureVerificationResult(
    bool Success,
    IReadOnlyList<string> Mismatches);

/// <summary>
/// 从当前模型 authoring 真源重算基础 DTO 签名，并校验签名清单与落盘生成物是否一致。
/// 该类型不加载模块程序集，适合作为 Host 开发态启动前的轻量阻断检查。
/// </summary>
public sealed class BaseDtoSignatureVerifier
{
    private static readonly Regex SignatureEntryPattern = new(
        "\\[\"(?<name>[^\"]+)\"\\]\\s*=\\s*\"(?<signature>sha256:[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 比对期望签名与当前生成物签名，返回所有不一致的基础 DTO 名称。
    /// </summary>
    public bool Verify(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        out IReadOnlyList<string> mismatches)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        mismatches = expected
            .Where(pair => !actual.TryGetValue(pair.Key, out var value)
                || !string.Equals(value, pair.Value, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();

        return mismatches.Count == 0;
    }

    /// <summary>
    /// 从当前模型真源重新生成预期签名，并同时校验签名 provider 与已落盘 DTO 文件。
    /// </summary>
    public BaseDtoSignatureVerificationResult VerifyRepository(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var fullRepositoryRoot = Path.GetFullPath(repositoryRoot);
        var generatedRoot = Path.Combine(fullRepositoryRoot, "server", "modules");
        if (!Directory.Exists(generatedRoot))
        {
            return new BaseDtoSignatureVerificationResult(false, ["server/modules"]);
        }

        var expectedFromModelProjection = BuildExpectedSignaturesFromModelProjection(fullRepositoryRoot);
        var providerSignatures = new Dictionary<string, string>(StringComparer.Ordinal);
        var generatedFileSignatures = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var providerPath in Directory.EnumerateFiles(
                     generatedRoot,
                     "*BaseDtoSignatureProvider.g.cs",
                     SearchOption.AllDirectories))
        {
            var providerDirectory = Path.GetDirectoryName(providerPath)
                ?? throw new InvalidOperationException($"无法定位签名 provider 所在目录: {providerPath}");

            foreach (Match match in SignatureEntryPattern.Matches(File.ReadAllText(providerPath)))
            {
                var dtoName = match.Groups["name"].Value;
                providerSignatures[dtoName] = match.Groups["signature"].Value;

                var dtoPath = Path.Combine(providerDirectory, dtoName + ".g.cs");
                generatedFileSignatures[dtoName] = File.Exists(dtoPath)
                    ? BaseDtoSourceRenderer.ComputeSignature(File.ReadAllText(dtoPath))
                    : string.Empty;
            }
        }

        foreach (var dtoPath in Directory.EnumerateFiles(
                     generatedRoot,
                     "*Dto.g.cs",
                     SearchOption.AllDirectories))
        {
            var dtoName = ResolveGeneratedDtoName(dtoPath);
            generatedFileSignatures.TryAdd(dtoName, BaseDtoSourceRenderer.ComputeSignature(File.ReadAllText(dtoPath)));
        }

        return VerifyGeneratedArtifacts(
            expectedFromModelProjection,
            providerSignatures,
            generatedFileSignatures);
    }

    internal BaseDtoSignatureVerificationResult VerifyGeneratedArtifactsForTests(
        IReadOnlyDictionary<string, string> expectedFromModelProjection,
        IReadOnlyDictionary<string, string> providerSignatures,
        IReadOnlyDictionary<string, string> generatedFileSignatures)
    {
        return VerifyGeneratedArtifacts(expectedFromModelProjection, providerSignatures, generatedFileSignatures);
    }

    private static BaseDtoSignatureVerificationResult VerifyGeneratedArtifacts(
        IReadOnlyDictionary<string, string> expectedFromModelProjection,
        IReadOnlyDictionary<string, string> providerSignatures,
        IReadOnlyDictionary<string, string> generatedFileSignatures)
    {
        var mismatches = new SortedSet<string>(StringComparer.Ordinal);

        AddSignatureMismatches(expectedFromModelProjection, providerSignatures, mismatches);
        AddSignatureMismatches(providerSignatures, generatedFileSignatures, mismatches);

        foreach (var providerOnlyDto in providerSignatures.Keys.Where(dto => !expectedFromModelProjection.ContainsKey(dto)))
        {
            mismatches.Add(providerOnlyDto);
        }

        foreach (var generatedOnlyDto in generatedFileSignatures.Keys.Where(dto =>
                     !expectedFromModelProjection.ContainsKey(dto)
                     || !providerSignatures.ContainsKey(dto)))
        {
            mismatches.Add(generatedOnlyDto);
        }

        return new BaseDtoSignatureVerificationResult(mismatches.Count == 0, mismatches.ToArray());
    }

    private static void AddSignatureMismatches(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        ISet<string> mismatches)
    {
        foreach (var pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out var value)
                || !string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                mismatches.Add(pair.Key);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> BuildExpectedSignaturesFromModelProjection(string repositoryRoot)
    {
        var modulesRoot = Path.Combine(repositoryRoot, "modules");
        if (!Directory.Exists(modulesRoot))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var storageTypeMapPath = Path.Combine(
            repositoryRoot,
            "server",
            "platform-modeling",
            "metadata",
            "storage-types.json");
        var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(storageTypeMapPath);
        var modelLoader = new JsonModelMetadataDocumentLoader();
        var validator = new ModelMetadataValidator();
        var projector = new ModelMetadataProjector();
        var generator = new ModelDtoGenerator();
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var moduleRoot in Directory.EnumerateDirectories(modulesRoot).OrderBy(static path => path, StringComparer.Ordinal))
        {
            var moduleId = Path.GetFileName(moduleRoot);
            var modelDirectory = Path.Combine(moduleRoot, "metadata", "models");
            if (!Directory.Exists(modelDirectory))
            {
                continue;
            }

            foreach (var modelPath in Directory.EnumerateFiles(modelDirectory, "*.model.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                var document = modelLoader.LoadFromFile(modelPath);
                validator.Validate(document, typeMap);
                var definition = projector.Project(document, typeMap, "SqlServer");
                var enumClrTypeMap = LoadEnumClrTypeMap(document);
                var sourceCode = BaseDtoSourceRenderer.WrapInNamespace(
                    generator.Generate(definition, enumClrTypeMap),
                    $"ICP.Platform.Modules.{moduleId}.DTOs",
                    ResolveUsingNamespaces(moduleId, enumClrTypeMap));

                signatures[definition.Name + "Dto"] = BaseDtoSourceRenderer.ComputeSignature(sourceCode);
            }
        }

        return signatures;
    }

    private static IReadOnlyDictionary<string, string> LoadEnumClrTypeMap(ModelMetadataDocument document)
    {
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

    private static string ResolveGeneratedDtoName(string dtoPath)
    {
        var fileName = Path.GetFileName(dtoPath);
        const string generatedSuffix = ".g.cs";
        return fileName.EndsWith(generatedSuffix, StringComparison.Ordinal)
            ? fileName[..^generatedSuffix.Length]
            : Path.GetFileNameWithoutExtension(dtoPath);
    }
}
