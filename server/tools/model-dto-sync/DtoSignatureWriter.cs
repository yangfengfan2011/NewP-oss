using System.Text;
using ICP.Platform.Persistence.DevSync.Rendering;

namespace ICP.Platform.Tools.ModelDtoSync;

public sealed class DtoSignatureWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>
    /// 写入模块基础 DTO 签名清单，供后续开发态同步在启动前校验生成物是否仍匹配模型真源。
    /// </summary>
    public string Write(string outputDirectory, string moduleId, IReadOnlyDictionary<string, string> signatures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(signatures);

        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, moduleId + "BaseDtoSignatureProvider.g.cs");
        File.WriteAllText(outputPath, Render(moduleId, signatures), Utf8NoBom);
        return outputPath;
    }

    /// <summary>
    /// 计算生成源码的稳定 SHA-256 签名；输入必须是写入文件的同一份源码内容。
    /// </summary>
    public static string ComputeSignature(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        return BaseDtoSourceRenderer.ComputeSignature(sourceCode);
    }

    private static string Render(string moduleId, IReadOnlyDictionary<string, string> signatures)
    {
        var builder = new StringBuilder();
        builder.Append("using System;\n");
        builder.Append("using System.Collections.Generic;\n");
        builder.Append('\n');
        builder.Append($"namespace ICP.Platform.Modules.{moduleId}.DTOs.Generated;\n");
        builder.Append('\n');
        builder.Append($"public sealed class {moduleId}BaseDtoSignatureProvider\n");
        builder.Append("{\n");
        builder.Append("    public IReadOnlyDictionary<string, string> GetSignatures() =>\n");
        builder.Append("        new Dictionary<string, string>(StringComparer.Ordinal)\n");
        builder.Append("        {\n");

        foreach (var signature in signatures.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            builder.Append($"            [\"{signature.Key}\"] = \"{signature.Value}\",\n");
        }

        builder.Append("        };\n");
        builder.Append("}\n");
        return builder.ToString();
    }
}
