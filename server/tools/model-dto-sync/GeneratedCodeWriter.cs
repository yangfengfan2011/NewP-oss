using System.Text;

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
        return sourceCode.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    // 基础 DTO 仍落在模块 DTO 命名空间，避免任务 6 替换手写 DTO 后破坏既有引用。
    internal static string WrapInNamespace(
        string sourceCode,
        string namespaceName,
        IReadOnlyList<string>? usingNamespaces = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        var normalizedSourceCode = NormalizeLineEndings(sourceCode);
        if (usingNamespaces is null || usingNamespaces.Count == 0)
        {
            return NormalizeLineEndings($"namespace {namespaceName};\n\n{normalizedSourceCode}");
        }

        var usings = string.Join(
            "\n",
            usingNamespaces
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .Select(static item => $"using {item};"));

        return NormalizeLineEndings($"{usings}\n\nnamespace {namespaceName};\n\n{normalizedSourceCode}");
    }
}
