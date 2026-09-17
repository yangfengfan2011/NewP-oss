using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>在组织缓存的同一写协调与数据库事务中登记资格中断；缓存仍独占候选构造和发布。</summary>
public sealed class OrganizationAdmissionCommitParticipant(OrganizationCache cache, IAuthenticationSessionBatchRevoker sessions)
    : IPlatformCommitParticipant
{
    public string Name => cache.Name;

    /// <inheritdoc/>
    public async ValueTask<IPlatformCommitScope> BeginAsync(CancellationToken cancellationToken)
    {
        var inner = await cache.BeginAsync(cancellationToken);
        try { return new Scope(inner, await cache.ReadAsync(cancellationToken), sessions); }
        catch { await inner.DisposeAsync(); throw; }
    }

    private sealed class Scope(IPlatformCommitScope inner, OrganizationSnapshot before, IAuthenticationSessionBatchRevoker sessions)
        : IPlatformCommitScope
    {
        private bool _changed;

        /// <inheritdoc/>
        public async ValueTask PrepareAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
        {
            _changed = changes.Count != 0;
            if (!_changed) return;
            await inner.PrepareAsync(changes, cancellationToken);
            var overlay = changes.GroupBy(change => (change.ModelKey, change.Id))
                .ToDictionary(group => group.Key, group => group.Last().After);
            var interrupted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var personId in AffectedPersons(before, changes))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = Resolve(before, personId, null);
                if (OrganizationAdmissionPolicy.IsEligible(previous)
                    && !OrganizationAdmissionPolicy.IsEligible(Resolve(before, personId, overlay))
                    && previous!.Person.GetValueOrDefault("AccountEID") is string accountId)
                    interrupted.Add(accountId);
            }
            if (interrupted.Count == 0) return;
            var actor = changes.Select(change => (change.After ?? change.Before)?.GetValueOrDefault("UpdatedBy") as string)
                .First(value => !string.IsNullOrWhiteSpace(value))!;
            await sessions.RevokeAccountsAsync(interrupted, "organization-admission-interrupted", actor, cancellationToken);
        }

        /// <inheritdoc/>
        public void BlockReads() { if (_changed) inner.BlockReads(); }
        /// <inheritdoc/>
        public ValueTask PublishAsync() => _changed ? inner.PublishAsync() : ValueTask.CompletedTask;
        /// <inheritdoc/>
        public void MarkUnavailable() => inner.MarkUnavailable();
        /// <inheritdoc/>
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    // 使用发布快照的关系索引和本事务直接变更收集影响人员；不扫描全组织或逐账号读取数据库。
    private static HashSet<string> AffectedPersons(OrganizationSnapshot snapshot, IReadOnlyList<PlatformRecordChange> changes)
    {
        var people = new HashSet<string>(StringComparer.Ordinal);
        var departments = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            switch (change.ModelKey)
            {
                case "Organization/Person": people.Add(change.Id); break;
                case "Organization/Company":
                    if (snapshot.PersonsByCompany.TryGetValue(change.Id, out var companyPeople)) people.UnionWith(companyPeople);
                    break;
                case "Organization/Department": departments.Add(change.Id); break;
            }
        }
        var pending = new Queue<string>(departments);
        while (pending.TryDequeue(out var id))
        {
            if (snapshot.PersonsByDepartment.TryGetValue(id, out var departmentPeople)) people.UnionWith(departmentPeople);
            if (snapshot.ChildDepartments.TryGetValue(id, out var children))
                foreach (var child in children) if (departments.Add(child)) pending.Enqueue(child);
        }
        return people;
    }

    // 仅将实际持久化后的记录叠加到同一原快照，解析受影响人员的前后资格；不维护第二套缓存索引。
    private static CurrentOrganization? Resolve(OrganizationSnapshot snapshot, string personId,
        IReadOnlyDictionary<(string ModelKey, string Id), IReadOnlyDictionary<string, object?>?>? overlay)
    {
        var person = Find("Person", personId, snapshot.Persons);
        if (person is null) return null;
        var company = Find("Company", person.GetValueOrDefault("CompanyId") as string, snapshot.Companies);
        var department = Find("Department", person.GetValueOrDefault("DepartmentId") as string, snapshot.Departments);
        var ancestors = new List<IReadOnlyDictionary<string, object?>>();
        var current = department;
        while (current?.GetValueOrDefault("ParentDepartmentId") is string parent)
        {
            current = Find("Department", parent, snapshot.Departments);
            if (current is null) return null;
            ancestors.Add(current);
        }
        return new(person, company, department, ancestors);

        // null 表示真实删除或准备阶段，而非缓存故障降级。
        IReadOnlyDictionary<string, object?>? Find(string model, string? id, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> records) =>
            id is null ? null : overlay is not null && overlay.TryGetValue(($"Organization/{model}", id), out var row)
                ? row : records.GetValueOrDefault(id);
    }
}
