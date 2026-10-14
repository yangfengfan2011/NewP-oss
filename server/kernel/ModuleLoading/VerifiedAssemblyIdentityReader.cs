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
    // 同步解析程序集身份与 MVID，返回前释放读取资源，不保留输入字节引用。
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

    // 只遍历 PE 中精确引用的 owner Contracts；同名版本冲突拒绝，visited 使循环引用终止。
    internal static IReadOnlyList<(VerifiedAssemblyIdentity Identity, byte[] Bytes)> ReadOwnerContractClosure(
        ReadOnlyMemory<byte> entryImage,
        Func<string, byte[]> readContract)
    {
        var pending = new Queue<ReadOnlyMemory<byte>>();
        var contracts = new Dictionary<string, (VerifiedAssemblyIdentity Identity, byte[] Bytes)>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(entryImage);
        while (pending.TryDequeue(out var image))
        {
            foreach (var requested in ReadOwnerContractReferences(image))
            {
                var name = requested.Name!;
                if (contracts.TryGetValue(name, out var existing))
                {
                    if (!VerifiedAssemblyIdentityPolicy.MatchesExact(existing.Identity.AssemblyName, requested))
                        throw new InvalidDataException($"Referenced shared contract '{requested.FullName}' conflicts with another reference.");
                    continue;
                }
                var bytes = readContract(name);
                var identity = Read(bytes);
                if (!VerifiedAssemblyIdentityPolicy.MatchesExact(identity.AssemblyName, requested))
                    throw new InvalidDataException($"Referenced shared contract '{requested.FullName}' differs from its captured image identity.");
                contracts.Add(name, (identity, bytes));
                pending.Enqueue(bytes);
            }
        }
        return contracts.Values.ToArray();
    }

    // 读取引用身份而不执行 PE；平台稳定共享依赖沿既有策略，其他合同只允许简单文件名。
    private static IReadOnlyList<AssemblyName> ReadOwnerContractReferences(ReadOnlyMemory<byte> image)
    {
        using var stream = MemoryMarshal.TryGetArray(image, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(image.ToArray(), writable: false);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var references = new List<AssemblyName>();
        foreach (var handle in metadata.AssemblyReferences)
        {
            var reference = metadata.GetAssemblyReference(handle);
            var name = new AssemblyName
            {
                Name = metadata.GetString(reference.Name),
                Version = reference.Version,
                CultureName = reference.Culture.IsNil ? null : metadata.GetString(reference.Culture)
            };
            if (!reference.PublicKeyOrToken.IsNil)
            {
                var key = metadata.GetBlobBytes(reference.PublicKeyOrToken);
                if (reference.Flags.HasFlag(AssemblyFlags.PublicKey)) name.SetPublicKey(key);
                else name.SetPublicKeyToken(key);
            }
            if (reference.Flags.HasFlag(AssemblyFlags.Retargetable)) name.Flags |= AssemblyNameFlags.Retargetable;
            if (reference.Flags.HasFlag(AssemblyFlags.WindowsRuntime)) name.ContentType = AssemblyContentType.WindowsRuntime;
            if (!name.Name!.EndsWith(".Contracts", StringComparison.OrdinalIgnoreCase)
                || VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(name)) continue;
            if (name.Name.IndexOfAny(['/', '\\', ':']) >= 0)
                throw new InvalidDataException($"Referenced shared contract '{name.FullName}' is not a simple assembly filename.");
            references.Add(name);
        }
        return references;
    }

#pragma warning disable SYSLIB0037
    // 按 PE 机器类型映射程序集架构，I386 另由 Requires32Bit 区分 X86 与 MSIL。
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
