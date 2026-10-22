using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Presentation;

namespace ICP.Platform.Modules.Audit.Services;

/// <summary>仅把已重验资格的普通安全DTO写入现有XLSX writer，不引入第二查询或管理员字段。</summary>
public sealed class ObjectHistoryExport(IObjectHistoryQuery history, StandardCrudXlsxWriter writer,
    IRuntimeTextCatalog texts, IRequestPresentationContextAccessor presentation) : IObjectHistoryExport
{
    /// <summary>选中与全部共用安全集合；取消不返回部分文件，writer只取得标量白名单。</summary>
    public async Task<ObjectHistoryFile?> ExportAsync(string resourceKey, string objectId, ObjectHistoryOutputRequest request,
        CancellationToken token = default)
    {
        var items = await history.OutputAsync(resourceKey, objectId, request, token);
        if (items is null) return null;
        token.ThrowIfCancellationRequested();
        var columns = new[] { "EventType", "OccurredAt", "ObjectType", "Actor", "Target", "Description", "IpAddress" }
            .Select(key => new StandardCrudExportColumn(key, key, Text(key))).ToArray();
        var rows = new List<IReadOnlyDictionary<string, object?>>(items.Count);
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            rows.Add(new Dictionary<string, object?>
            {
                ["EventType"] = item.EventType, ["OccurredAt"] = item.OccurredAt.ToString("O"), ["ObjectType"] = item.ObjectType,
                ["Actor"] = item.Actor.Name ?? Text("ActorUnavailable"),
                ["Target"] = item.Target.Name is null && item.Target.Code is null
                    ? Text(item.Target.NameSource == "unavailable" ? "NameSource_unavailable" : "Unrecorded")
                    : string.Join(" / ", new[] { item.Target.Name, item.Target.Code }.Where(value => value is not null))
                        + " (" + Text("NameSource_" + item.Target.NameSource) + ")",
                ["Description"] = texts.GetText(item.Description.Key, presentation.Current?.Locale,
                    item.Description.Parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal)),
                ["IpAddress"] = item.IpAddress ?? Text("Unrecorded")
            });
        }
        var content = writer.Write("Object history", columns, rows);
        token.ThrowIfCancellationRequested();
        return new("object-history.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", content);
    }

    private string Text(string key) => texts.GetText("ObjectHistory." + key, presentation.Current?.Locale);
}
