using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Repositories;

public interface IPersonUserBindingRepository
{
    ValueTask<PersonUserBindingState?> FindAsync(
        string personEid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅在 Person 仍启用且尚未绑定时登记永久绑定；不保存或提交外层事务。
    /// </summary>
    ValueTask<bool> TryBindAsync(
        string personEid,
        string accountEid,
        DateTime boundAt,
        string actorId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 登记 Person 启停变化；不保存或提交外层事务。
    /// </summary>
    ValueTask SetEnablementAsync(
        string personEid,
        bool isEnabled,
        string actorId,
        DateTime changedAt,
        CancellationToken cancellationToken = default);
}
