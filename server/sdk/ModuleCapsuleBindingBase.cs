using System.Reflection;
using System.Text.Json;
using ICP.Platform.Kernel.Abstractions;

namespace ICP.Platform.Sdk;

/// <summary>
/// C# 行为实现只暴露稳定 behavior key；模块身份、版本与权限由嵌入的根 capsule 拥有。
/// </summary>
public interface IModuleBehaviorBinding
{
    string BehaviorKey { get; }
}

/// <summary>
/// 把嵌入式 Module Capsule 适配到现有 IModule/IPermissionProvider 接口。
/// </summary>
public abstract class ModuleCapsuleBindingBase : ModuleBase, IPermissionProvider, IModuleBehaviorBinding
{
    private CapsuleDocument? _capsule;

    public abstract string BehaviorKey { get; }

    public sealed override string ModuleId => Capsule.ModuleId;

    public sealed override string DisplayName => Capsule.DisplayName;

    public sealed override string Version => Capsule.Version;

    public sealed override string? Description => Capsule.Description;

    public sealed override IReadOnlyList<string> Dependencies => Capsule.Dependencies;

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return Capsule.Security.Permissions
            .Select(permission => new PermissionDescriptor
            {
                Code = permission.Code,
                DisplayName = permission.DisplayName,
                Description = permission.Description,
                ModuleId = ModuleId,
                Group = permission.Group
            })
            .ToArray();
    }

    private CapsuleDocument Capsule => _capsule ??= LoadCapsule();

    private CapsuleDocument LoadCapsule()
    {
        var assembly = GetType().Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(static name => name.EndsWith(".newp.plugin.json", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        if (resourceNames.Length != 1)
        {
            throw new InvalidOperationException(
                $"Module behavior assembly '{assembly.GetName().Name}' must embed exactly one newp.plugin.json capsule.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceNames[0])
            ?? throw new InvalidOperationException($"Embedded module capsule '{resourceNames[0]}' could not be opened.");
        var capsule = JsonSerializer.Deserialize<CapsuleDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new JsonException("Embedded module capsule deserialized to null.");

        if (!string.Equals(capsule.Backend.BehaviorKey, BehaviorKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Backend behavior key '{BehaviorKey}' does not match capsule binding '{capsule.Backend.BehaviorKey}'.");
        }

        if (string.IsNullOrWhiteSpace(capsule.ModuleId)
            || string.IsNullOrWhiteSpace(capsule.Version)
            || string.IsNullOrWhiteSpace(capsule.DisplayName))
        {
            throw new JsonException("Embedded module capsule identity is incomplete.");
        }

        return capsule;
    }

    private sealed class CapsuleDocument
    {
        public string ModuleId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? Description { get; set; }
        public IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();
        public CapsuleBackend Backend { get; set; } = new();
        public CapsuleSecurity Security { get; set; } = new();
    }

    private sealed class CapsuleBackend
    {
        public string BehaviorKey { get; set; } = string.Empty;
    }

    private sealed class CapsuleSecurity
    {
        public IReadOnlyList<CapsulePermission> Permissions { get; set; } = Array.Empty<CapsulePermission>();
    }

    private sealed class CapsulePermission
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Group { get; set; }
    }
}
