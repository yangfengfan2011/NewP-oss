using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Audit.Contracts;

namespace ICP.Platform.Modules.Part;

/// <summary>在标准对象事务内登记零组件真实业务动作及名称、编码快照。</summary>
public sealed class PartBusinessRecorder(IBusinessAuditRecorder audit) : IStandardCrudCreationRecorder, IStandardCrudUpdateRecorder
{
    public string ResourceKey => "part.part";
    private static readonly string[] BusinessFields = ["ECODE", "ENAME", "Description"];

    /// <inheritdoc/>
    public ValueTask RegisterAsync(IReadOnlyDictionary<string, object?> created, CancellationToken cancellationToken = default) =>
        RegisterEventAsync("part.created", created, cancellationToken);

    /// <inheritdoc/>
    public ValueTask RegisterAsync(IReadOnlyDictionary<string, object?> before, IReadOnlyDictionary<string, object?> after,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return BusinessFields.Any(field => !Equals(before.GetValueOrDefault(field), after.GetValueOrDefault(field)))
            ? RegisterEventAsync("part.updated", after, cancellationToken) : ValueTask.CompletedTask;
    }

    /// <summary>使用所属删除行为在成功前保留的实际文本登记删除事实。</summary>
    public ValueTask RegisterDeletedAsync(IReadOnlyDictionary<string, object?> deleted, CancellationToken cancellationToken = default) =>
        RegisterEventAsync("part.deleted", deleted, cancellationToken);

    // 只登记两个已声明的历史文本字段；不采集客户端载荷或扩大普通履历输出。
    private ValueTask RegisterEventAsync(string eventType, IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken) =>
        audit.RegisterAsync(new(eventType, ResourceKey, (string)row["EID"]!,
            [new("ObjectName", null, BusinessAuditValue.Text(row.GetValueOrDefault("ENAME") as string)),
             new("ObjectCode", null, BusinessAuditValue.Text(row.GetValueOrDefault("ECODE") as string))]), cancellationToken);
}
