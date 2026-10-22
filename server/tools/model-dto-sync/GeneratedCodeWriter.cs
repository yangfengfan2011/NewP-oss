using System.Text;
using ICP.Platform.Modeling.Rendering;

namespace ICP.Platform.Tools.ModelDtoSync;

public sealed class GeneratedCodeWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>
    /// 将生成源码写入目标路径，并返回规范化后的完整源码内容。
    /// </summary>
    public string Write(string outputPath, string sourceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(sourceCode);

        var normalizedSourceCode = NormalizeLineEndings(sourceCode);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, normalizedSourceCode, Utf8NoBom);
        return normalizedSourceCode;
    }

    // 生成物签名跨平台复现依赖稳定源码文本，这里统一收口 LF 换行。
    internal static string NormalizeLineEndings(string sourceCode)
    {
        return BaseDtoSourceRenderer.NormalizeLineEndings(sourceCode);
    }

    /// <summary>
    /// 基础 DTO 仍落在模块 DTO 命名空间；包装规则由 Modeling 共享 helper 统一提供。
    /// </summary>
    public static string WrapInNamespace(
        string sourceCode,
        string namespaceName,
        IReadOnlyList<string>? usingNamespaces = null)
    {
        return BaseDtoSourceRenderer.WrapInNamespace(sourceCode, namespaceName, usingNamespaces);
    }
}
