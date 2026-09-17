using System.Globalization;
using System.Text.Json;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Presentation;
using ICP.Platform.Modeling.DisplayPlanning;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Audit.Services;

/// <summary>存储侧查询输入，分页与时间范围不影响历史事实。</summary>
public sealed record BusinessAuditFilter(int PageIndex = 1, int PageSize = 20, string? Resource = null, string? TargetId = null,
    string? ActorId = null, string? EventType = null, DateTime? From = null, DateTime? To = null);
/// <summary>标准宿主列表协议。</summary>
public sealed record BusinessAuditPage(IReadOnlyList<Dictionary<string, object?>> Items, int Total, int PageIndex, int PageSize, string SortBy = "OccurredAt", string SortDirection = "desc");
/// <summary>Audit 自有查询；先在数据库过滤、计数和稳定分页，再批量附加当前名称。</summary>
public sealed class BusinessAuditQuery(EfPlatformDbContext db, IEnumerable<IBusinessAuditNameDirectory> directories,
    IUserDisplayNameDirectory userNames, ITemporalDisplayFormatter formatter, IRequestPresentationContextAccessor presentation)
{
    /// <summary>查询失败继续传播，只有真正无记录才返回空页。</summary>
    public async Task<BusinessAuditPage> ListAsync(BusinessAuditFilter filter, CancellationToken cancellationToken = default)
    {
        var size = filter.PageSize <= 0 ? 20 : filter.PageSize;
        var page = Math.Clamp(filter.PageIndex, 1, int.MaxValue / size);
        var query = Rows();
        if (!string.IsNullOrWhiteSpace(filter.Resource)) query = query.Where(r => EF.Property<string>(r, "Resource") == filter.Resource);
        if (!string.IsNullOrWhiteSpace(filter.TargetId)) query = query.Where(r => EF.Property<string>(r, "TargetId") == filter.TargetId);
        if (!string.IsNullOrWhiteSpace(filter.ActorId)) query = query.Where(r => EF.Property<string>(r, "ActorId") == filter.ActorId);
        if (!string.IsNullOrWhiteSpace(filter.EventType)) query = query.Where(r => EF.Property<string>(r, "EventType") == filter.EventType);
        if (filter.From is { } from) query = query.Where(r => EF.Property<DateTime>(r, "OccurredAt") >= from);
        if (filter.To is { } to) query = query.Where(r => EF.Property<DateTime>(r, "OccurredAt") <= to);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(r => EF.Property<DateTime>(r, "OccurredAt"))
            .ThenByDescending(r => EF.Property<string>(r, "EID")).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        await ProjectAsync(rows, cancellationToken);
        return new(rows, total, page, size);
    }
    /// <summary>目标对象不存在时仍只按记录 ID 读取 Audit 自有事实。</summary>
    public async Task<Dictionary<string, object?>?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = await Rows().SingleOrDefaultAsync(r => EF.Property<string>(r, "EID") == id, cancellationToken);
        if (row is not null) await ProjectAsync([row], cancellationToken);
        return row;
    }
    // 只从 Audit 当前编译计划的 shared model 读取，不连接业务实体。
    private IQueryable<Dictionary<string, object?>> Rows() => db.Set<Dictionary<string, object?>>("Audit/BusinessChange").AsNoTracking();
    // 每类目录只调用一次，非关键目录故障只降级名称；关键缓存及数据库故障继续传播。
    private async Task ProjectAsync(List<Dictionary<string, object?>> rows, CancellationToken token)
    {
        var actors = rows.SelectMany(r => new[] { "ActorId", "CreatedBy", "UpdatedBy" }.Select(field => r.GetValueOrDefault(field) as string))
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var names = await ResolveAsync(() => userNames.FindManyAsync(actors, token), token);
        var fieldsByRow = rows.ToDictionary(r => (string)r["EID"]!, r => JsonSerializer.Deserialize<BusinessAuditField[]>((string)r["FieldsJson"]!)!);
        var values = fieldsByRow.Values.SelectMany(f => f).SelectMany(f => new[] { f.Before, f.After }).OfType<BusinessAuditValue>().ToList();
        values.AddRange(rows.Select(r => new BusinessAuditValue((string)r["Resource"]!, (string)r["TargetId"]!)));
        var resolved = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var ids = values.Where(v => v.Kind == directory.Kind && v.Value is not null).Select(v => v.Value!).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length > 0) resolved[directory.Kind] = await ResolveAsync(() => directory.FindManyAsync(ids, token), token);
        }
        foreach (var row in rows)
        {
            foreach (var field in new[] { "CreatedBy", "UpdatedBy" })
                row[field + "_DisplayValue"] = row.GetValueOrDefault(field) is string actorId ? names.GetValueOrDefault(actorId) : null;
            foreach (var field in new[] { "CreatedAt", "UpdatedAt" })
                row[field + "_DisplayValue"] = row.GetValueOrDefault(field) is DateTime instant && presentation.Current is { } stampContext
                    ? formatter.FormatInstant(DateTime.SpecifyKind(instant, DateTimeKind.Utc), TemporalDisplayFormat.DateTime, stampContext) : null;
            row["ActorId_DisplayValue"] = names.GetValueOrDefault((string)row["ActorId"]!);
            row["TargetId_DisplayValue"] = resolved.GetValueOrDefault((string)row["Resource"]!)?.GetValueOrDefault((string)row["TargetId"]!);
            var occurred = DateTime.SpecifyKind((DateTime)row["OccurredAt"]!, DateTimeKind.Utc);
            row["OccurredAt"] = occurred;
            row["OccurredAt_DisplayValue"] = presentation.Current is { } context ? formatter.FormatInstant(occurred, TemporalDisplayFormat.DateTime, context) : null;
            row["Changes"] = fieldsByRow[(string)row["EID"]!].Select(f => new { f.Field, f.Before, f.After,
                BeforeDisplay = Display(f.Before, resolved), AfterDisplay = Display(f.After, resolved) }).ToArray();
            row.Remove("FieldsJson");
        }
    }
    // 展示使用平台日期/时间规则；直接文本从不按当前名称改写。
    private string? Display(BusinessAuditValue? value, Dictionary<string, IReadOnlyDictionary<string, string>> names)
    {
        if (value?.Value is not { } raw) return null;
        if (presentation.Current is { } context)
        {
            if (value.Kind == "calendarDate") return formatter.FormatCalendarDate(DateOnly.ParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture), context);
            if (value.Kind == "instant") return formatter.FormatInstant(DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), TemporalDisplayFormat.DateTime, context);
        }
        return names.GetValueOrDefault(value.Kind)?.GetValueOrDefault(raw) ?? raw;
    }
    // 非关键名称故障允许 ID 降级；缓存不可用与取消继续传播，避免伪装成名称缺失。
    private static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(Func<Task<IReadOnlyDictionary<string, string>>> read, CancellationToken token)
    {
        try { return await read(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not ICP.Platform.Persistence.Transactions.PlatformCacheUnavailableException)
        { token.ThrowIfCancellationRequested(); return new Dictionary<string, string>(); }
    }
}
