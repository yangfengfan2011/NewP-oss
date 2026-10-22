using System.Security.Cryptography;
using System.Text.Json;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

/// <summary>平台发行方维护的公共运行时契约与共享依赖身份；不由业务构建猜测。</summary>
public static class PlatformRuntimeReleaseIdentity
{
    public static byte[] Descriptor => ReadDescriptor();
    public static PlatformRuntimeRelease Current { get; } = Create();
    internal static byte[] StorageTypeMap => ReadResource("NewP.NativeStorageTypes.json");

    // 从唯一受控发行声明读取，不允许包自身给 Host 增加支持的发行身份。
    private static byte[] ReadDescriptor()
        => ReadResource("NewP.PlatformRuntimeRelease.json");

    // 编译的受控资源来自现有平台真源，不在业务包内复制一套存储规则。
    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(PlatformRuntimeReleaseIdentity).Assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // 发行声明的确切字节形成摘要，业务 HEAD 和包父版本均未进入输入。
    private static PlatformRuntimeRelease Create()
    {
        var bytes = ReadDescriptor();
        using var document = JsonDocument.Parse(bytes);
        return new(document.RootElement.GetProperty("version").GetString()!, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
