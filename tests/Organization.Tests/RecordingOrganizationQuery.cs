using ICP.Platform.Organization.Contracts;
using ICP.Platform.Modules.Organization.Models;
namespace ICP.Platform.Modules.Organization.Tests;

internal sealed class RecordingOrganizationQuery : IOrganizationQuery, IOrganizationReadView
{
    private readonly Dictionary<OrganizationRecordKind, Dictionary<string, IReadOnlyDictionary<string, object?>>> _rows = [];
    public int Reads { get; private set; }
    public Dictionary<OrganizationRecordKind, IReadOnlyCollection<string>> Batches { get; } = [];
    public RecordingOrganizationQuery(IEnumerable<Company>? companies = null, IEnumerable<Department>? departments = null, IEnumerable<Position>? positions = null)
    {
        _rows[OrganizationRecordKind.Company] = (companies ?? []).ToDictionary(r => r.Id, r => Row(r.Id, r.Name, r.IsEnabled));
        _rows[OrganizationRecordKind.Department] = (departments ?? []).ToDictionary(r => r.Id, r => Row(r.Id, r.Name, r.IsEnabled));
        _rows[OrganizationRecordKind.Position] = (positions ?? []).ToDictionary(r => r.Id, r => Row(r.Id, r.Name, r.IsEnabled));
    }
    private static IReadOnlyDictionary<string, object?> Row(string id, string name, bool enabled) =>
        new Dictionary<string, object?> { ["EID"] = id, ["ENAME"] = name, ["IsEnabled"] = enabled };
    public ValueTask<IOrganizationReadView> ReadAsync(CancellationToken cancellationToken = default) { Reads++; return ValueTask.FromResult<IOrganizationReadView>(this); }
    public IReadOnlyDictionary<string, object?>? Find(OrganizationRecordKind kind, string id) => _rows[kind].GetValueOrDefault(id);
    public IReadOnlyDictionary<string, object?>? FindCompanyByCode(string code) => throw new NotSupportedException();
    public CurrentOrganization? FindByAccount(string accountId) => throw new NotSupportedException();
    public IReadOnlyDictionary<string, OrganizationName> ResolveNames(OrganizationRecordKind kind, IReadOnlyCollection<string> ids)
    {
        Batches.Add(kind, ids);
        return ids.Where(_rows[kind].ContainsKey).ToDictionary(id => id, id => new OrganizationName((string)_rows[kind][id]["ENAME"]!, _rows[kind][id]["IsEnabled"] is true));
    }
}
