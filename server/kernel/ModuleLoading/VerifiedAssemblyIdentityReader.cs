using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace ICP.Platform.Kernel.ModuleLoading;

/// <summary>受控装载用于核对映像与已加载程序集的精确身份：程序集名（含架构）与模块 MVID。</summary>
internal sealed record VerifiedAssemblyIdentity(AssemblyName AssemblyName, Guid Mvid);

/// <summary>
/// 从 PE 映像字节同步解析 <see cref="VerifiedAssemblyIdentity"/>。只读取元数据，不装载程序集，
/// 返回前释放流与 <see cref="PEReader"/>，不保留对输入的引用；调用方须保证解析期间字节不被修改。
/// </summary>
internal static class VerifiedAssemblyIdentityReader
{
    internal static VerifiedAssemblyIdentity Read(ReadOnlyMemory<byte> image)
    {
        // 数组支撑的输入按原段只读包装，不复制；其他来源才复制一份。
        using var stream = MemoryMarshal.TryGetArray(image, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(image.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        var assemblyName = new AssemblyName
        {
            Name = metadata.GetString(definition.Name),
            Version = definition.Version,
            CultureName = definition.Culture.IsNil ? null : metadata.GetString(definition.Culture)
        };
#pragma warning disable SYSLIB0037
        assemblyName.ProcessorArchitecture = ReadProcessorArchitecture(peReader.PEHeaders);
#pragma warning restore SYSLIB0037
        if (!definition.PublicKey.IsNil)
        {
            assemblyName.SetPublicKey(metadata.GetBlobBytes(definition.PublicKey));
        }
        if (definition.Flags.HasFlag(AssemblyFlags.Retargetable))
        {
            assemblyName.Flags |= AssemblyNameFlags.Retargetable;
        }
        if (definition.Flags.HasFlag(AssemblyFlags.WindowsRuntime))
        {
            assemblyName.ContentType = AssemblyContentType.WindowsRuntime;
        }

        return new VerifiedAssemblyIdentity(
            assemblyName,
            metadata.GetGuid(metadata.GetModuleDefinition().Mvid));
    }

#pragma warning disable SYSLIB0037
    private static ProcessorArchitecture ReadProcessorArchitecture(PEHeaders headers) =>
        headers.CoffHeader.Machine switch
        {
            Machine.I386 when headers.CorHeader?.Flags.HasFlag(CorFlags.Requires32Bit) == true => ProcessorArchitecture.X86,
            Machine.I386 => ProcessorArchitecture.MSIL,
            Machine.Amd64 => ProcessorArchitecture.Amd64,
            Machine.IA64 => ProcessorArchitecture.IA64,
            Machine.Arm or Machine.ArmThumb2 or Machine.Arm64 => ProcessorArchitecture.Arm,
            _ => ProcessorArchitecture.None
        };
#pragma warning restore SYSLIB0037
}
