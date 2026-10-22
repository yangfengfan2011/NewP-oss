using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
namespace ICP.Platform.Modules.Audit.Services;
/// <summary>仅登记当前 scoped 事务中的业务事实，保存与提交由平台事务执行器持有。</summary>
public sealed class BusinessAuditRecorder(EfPlatformDbContext db, IEntityAuditValueProvider audit, IPlatformObjectIdGenerator ids,
    IHttpContextAccessor? request) : IBusinessAuditRecorder
{
    /// <summary>保留非HTTP可信调用方的原构造；无请求来源时IP明确未记录。</summary>
    public BusinessAuditRecorder(EfPlatformDbContext db, IEntityAuditValueProvider audit, IPlatformObjectIdGenerator ids)
        : this(db, audit, ids, null) { }

    /// <inheritdoc/>
    public ValueTask RegisterAsync(BusinessAuditChange change, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Business audit requires a platform transaction.");
        ArgumentException.ThrowIfNullOrWhiteSpace(change.EventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(change.Resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(change.TargetId);
        if (change.Fields.Count == 0 || change.Fields.Select(f => f.Field).Distinct(StringComparer.Ordinal).Count() != change.Fields.Count)
            throw new ArgumentException("Business audit requires distinct declared fields.", nameof(change));
        var stamp = audit.Current();
        var traceId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(traceId)) throw new InvalidOperationException("Business audit requires a trusted trace.");
        db.Set<Dictionary<string, object?>>("BusinessChange").Add(new()
        {
            ["EID"] = ids.NewId("BusinessChange"), ["EventType"] = change.EventType,
            ["Resource"] = change.Resource, ["TargetId"] = change.TargetId,
            ["ActorId"] = stamp.ActorId, ["OccurredAt"] = stamp.OccurredAtUtc,
            // Host先处理显式可信代理；只读连接事实，绝不读取客户端转发头。
            ["IpAddress"] = request?.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            ["TraceId"] = traceId, ["FieldsJson"] = JsonSerializer.Serialize(change.Fields)
        });
        return ValueTask.CompletedTask;
    }
}
