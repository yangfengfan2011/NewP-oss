using System.Security.Cryptography;

namespace ICP.Platform.Kernel.PluginDiscovery;

/// <summary>
/// 插件哈希验证器
/// </summary>
public class PluginHashValidator
{
    public bool ValidateHash(string expectedHash, string filePath)
    {
        if (string.IsNullOrWhiteSpace(expectedHash) || string.IsNullOrWhiteSpace(filePath))
        {
            return true; // Skip validation if either is missing
        }

        if (!File.Exists(filePath))
        {
            return true; // Skip validation if file doesn't exist
        }

        var actualHash = ComputeSha256(filePath);
        return string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
    }

    public string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
