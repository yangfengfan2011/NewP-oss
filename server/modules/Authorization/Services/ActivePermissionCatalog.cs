using ICP.Platform.Kernel.Abstractions;
using System.Collections.Frozen;
namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>只由启动期权限同步替换的活动目录，历史数据库行不形成有效权限。</summary>
public sealed class ActivePermissionCatalog
{
    private FrozenSet<string> _codes = FrozenSet<string>.Empty;
    public IReadOnlySet<string> Codes => _codes;
    // 同步成功后一次发布规范代码；未初始化时安全关闭。
    internal void Publish(IEnumerable<PermissionDescriptor> descriptors) =>
        _codes = descriptors.Select(p => p.Code).ToFrozenSet(StringComparer.Ordinal);
}
