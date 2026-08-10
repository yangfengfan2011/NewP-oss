using System.Security.Cryptography;

namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// 计划编译阶段固定并校验的程序集映像。执行器只从该不可变副本装载代码。
/// </summary>
public sealed class VerifiedArtifactImage
{
    private readonly byte[] _bytes;

    /// <summary>
    /// 创建程序集映像的不可变副本；执行阶段仍会校验其摘要与计划声明一致。
    /// </summary>
    public VerifiedArtifactImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("Verified artifact image must not be empty.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(_bytes));
    }

    /// <summary>
    /// 映像内容的 SHA-256 摘要。
    /// </summary>
    public string Sha256 { get; }

    internal byte[] CopyBytes() => _bytes.ToArray();
}
