using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Document;

/// <summary>在原对象事务内初始化首版与 regular 快照；身份及审计只来自服务器。</summary>
public sealed class DocumentInitialIdentity(EfPlatformDbContext db, IPlatformObjectIdGenerator ids, IBusinessAuditRecorder audit)
    : IStandardCrudCreationRecorder, IStandardCrudUpdateRecorder
{
    public string ResourceKey => "document.document";
    private static readonly string[] AuditFields = ["CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
    private static readonly string[] SnapshotFields = ["ECODE", "ENAME", "Description"];

    /// <inheritdoc/>
    public async ValueTask RegisterAsync(IReadOnlyDictionary<string, object?> created, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        cancellationToken.ThrowIfCancellationRequested();
        // 共享 Dictionary CLR 类型的 Local 可能包含其他 Model；真实模型身份先于字段读取。
        var master = (Dictionary<string, object?>)db.ChangeTracker.Entries().Single(entry => entry.Metadata.Name == "Document"
            && Equals(entry.Property("EID").CurrentValue, created["EID"])).Entity;
        var version = AuditRow(created, ids.NewId("DocumentVersion"));
        var iteration = AuditRow(created, ids.NewId("DocumentIteration"));
        version["DocumentId"] = created["EID"];
        version["VersionOrdinal"] = 1L;
        version["VersionLabel"] = "1";
        version["State"] = "new";
        version["CurrentIterationId"] = iteration["EID"];
        iteration["VersionId"] = version["EID"];
        iteration["IterationOrdinal"] = 1L;
        iteration["Kind"] = "regular";
        iteration["DatasetTypeKey"] = "generic";
        foreach (var field in SnapshotFields) iteration[field] = created.GetValueOrDefault(field);
        master["LatestVersionId"] = version["EID"];
        db.Set<Dictionary<string, object?>>("DocumentVersion").Add(version);
        db.Set<Dictionary<string, object?>>("DocumentIteration").Add(iteration);
        await RegisterEventAsync("document.created", created, cancellationToken);
        await RegisterEventAsync("document.iteration.initialized", created, cancellationToken,
            [new("VersionLabel", null, BusinessAuditValue.Text((string)version["VersionLabel"]!)),
             new("IterationKind", null, BusinessAuditValue.Text((string)iteration["Kind"]!)),
             new("IterationOrdinal", null, BusinessAuditValue.Text(((long)iteration["IterationOrdinal"]!).ToString(System.Globalization.CultureInfo.InvariantCulture)))]);
    }

    /// <inheritdoc/>
    public async ValueTask RegisterAsync(IReadOnlyDictionary<string, object?> before, IReadOnlyDictionary<string, object?> after,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        var current = await ReadCurrentAsync((string)after["EID"]!, cancellationToken);
        foreach (var field in SnapshotFields) current.Iteration[field] = after.GetValueOrDefault(field);
        current.Iteration["UpdatedBy"] = after["UpdatedBy"];
        current.Iteration["UpdatedAt"] = after["UpdatedAt"];
        if (SnapshotFields.Any(field => !Equals(before.GetValueOrDefault(field), after.GetValueOrDefault(field))))
            await RegisterEventAsync("document.updated", after, cancellationToken);
    }

    /// <summary>登记删除成功前保留的真实文本；提交仍由所属对象事务决定。</summary>
    public ValueTask RegisterDeletedAsync(IReadOnlyDictionary<string, object?> deleted, CancellationToken cancellationToken = default) =>
        RegisterEventAsync("document.deleted", deleted, cancellationToken);

    // 名称和编码是本次事实快照，不从当前目录反推旧名称，也不采集任意字段载荷。
    private ValueTask RegisterEventAsync(string eventType, IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken,
        IReadOnlyList<BusinessAuditField>? facts = null) =>
        audit.RegisterAsync(new(eventType, ResourceKey, (string)row["EID"]!,
            [new("ObjectName", null, BusinessAuditValue.Text(row.GetValueOrDefault("ENAME") as string)),
             new("ObjectCode", null, BusinessAuditValue.Text(row.GetValueOrDefault("ECODE") as string)), .. facts ?? []]), cancellationToken);

    /// <summary>只沿服务端当前指针与真实回链读取；缺失或损坏不猜任意版次。</summary>
    public async Task<DocumentCurrentIdentity> ReadCurrentAsync(string documentId, CancellationToken cancellationToken = default, bool tracking = true)
    {
        var masters = db.Set<Dictionary<string, object?>>("Document").AsQueryable();
        var versions = db.Set<Dictionary<string, object?>>("DocumentVersion").AsQueryable();
        var iterations = db.Set<Dictionary<string, object?>>("DocumentIteration").AsQueryable();
        if (!tracking) { masters = masters.AsNoTracking(); versions = versions.AsNoTracking(); iterations = iterations.AsNoTracking(); }
        var master = await masters.SingleOrDefaultAsync(
            row => EF.Property<string>(row, "EID") == documentId, cancellationToken);
        if (master is null) throw new InvalidOperationException("Document.IdentityMissing");
        if (master.GetValueOrDefault("LatestVersionId") is not string versionId)
            throw new InvalidOperationException("Document.IdentityCorrupted");
        var version = await versions.SingleOrDefaultAsync(
            row => EF.Property<string>(row, "EID") == versionId, cancellationToken);
        if (version is null || !Equals(version["DocumentId"], documentId) || version.GetValueOrDefault("CurrentIterationId") is not string iterationId)
            throw new InvalidOperationException("Document.IdentityCorrupted");
        var iteration = await iterations.SingleOrDefaultAsync(
            row => EF.Property<string>(row, "EID") == iterationId, cancellationToken);
        if (iteration is null || !Equals(iteration["VersionId"], versionId) || !Equals(iteration["Kind"], "regular"))
            throw new InvalidOperationException("Document.IdentityCorrupted");
        return new(master, version, iteration);
    }

    // 下级审计与主对象来自同一次可信创建，不伪造历史或产生第二个审计来源。
    private static Dictionary<string, object?> AuditRow(IReadOnlyDictionary<string, object?> master, string id)
    {
        var row = AuditFields.ToDictionary(field => field, field => master[field], StringComparer.Ordinal);
        row["EID"] = id;
        return row;
    }

    // Recorder 只登记 tracker，不自行 Flush 或取得独立提交所有权。
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Document.TransactionRequired");
    }
}

/// <summary>Document 内部实际主对象、首版与当前 regular 的已核对回链。</summary>
public sealed record DocumentCurrentIdentity(Dictionary<string, object?> Master, Dictionary<string, object?> Version,
    Dictionary<string, object?> Iteration);
