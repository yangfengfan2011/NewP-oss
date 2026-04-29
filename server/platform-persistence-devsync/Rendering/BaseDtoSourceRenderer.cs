using System.Security.Cryptography;
using System.Text;

namespace ICP.Platform.Persistence.DevSync.Rendering;

/// <summary>
/// 提供基础 DTO 生成物的稳定源码包装与签名规则。
/// Host-facing 校验和 model-dto-sync 写入必须共用这里的规则，避免换行、namespace 或 hash 逻辑分叉。
/// </summary>
public static class BaseDtoSourceRenderer
{
    /// <summary>
    /// 将基础 DTO 裸源码包装到模块 DTO 命名空间中，并返回用于写盘和签名的规范化源码。
    /// </summary>
    public static string WrapInNamespace(
        string sourceCode,
        string namespaceName,
        IReadOnlyList<string>? usingNamespaces = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        var normalizedSourceCode = NormalizeLineEndings(sourceCode);
        if (usingNamespaces is null || usingNamespaces.Count == 0)
        {
            return NormalizeLineEndings($"#nullable enable annotations\n\nnamespace {namespaceName};\n\n{normalizedSourceCode}");
        }

        var usings = string.Join(
            "\n",
            usingNamespaces
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .Select(static item => $"using {item};"));

        return NormalizeLineEndings($"#nullable enable annotations\n\n{usings}\n\nnamespace {namespaceName};\n\n{normalizedSourceCode}");
    }

    /// <summary>
    /// 计算基础 DTO 源码的稳定签名；失败排查时优先确认调用方传入的是同一份规范化源码。
    /// </summary>
    public static string ComputeSignature(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        var normalized = NormalizeLineEndings(sourceCode);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 统一生成物换行为 LF，确保不同平台写盘后签名可复现。
    /// </summary>
    public static string NormalizeLineEndings(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        return sourceCode.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }
}
