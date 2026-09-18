using System.Collections.Immutable;
using ICP.Platform.Organization.Contracts;

namespace ICP.Platform.Modules.Organization.Caching;

/// <summary>在已校验的组织记录视图上统一解析人员的当前公司、部门及上级链。</summary>
internal static class CurrentOrganizationResolver
{
    // 解析只沿人员的直接关系和部门上级链读取，不扫描快照或重新校验候选。
    internal static CurrentOrganization? Resolve(string personId, OrganizationRelationshipRecordView records)
    {
        var person = records.Find(OrganizationRelationshipRecordKind.Person, personId);
        if (person is null) return null;
        var company = FindOptional(records, OrganizationRelationshipRecordKind.Company,
            person.GetValueOrDefault("CompanyId") as string);
        var department = FindOptional(records, OrganizationRelationshipRecordKind.Department,
            person.GetValueOrDefault("DepartmentId") as string);
        var ancestors = ImmutableArray.CreateBuilder<IReadOnlyDictionary<string, object?>>();
        var current = department;
        while (current?.GetValueOrDefault("ParentDepartmentId") is string parentId)
        {
            current = records.Find(OrganizationRelationshipRecordKind.Department, parentId);
            if (current is null) return null;
            ancestors.Add(current);
        }

        return new(person, company, department, ancestors.ToImmutable());
    }

    // 空标识表示合法准备态；非空标识沿当前记录视图的缺失语义读取。
    private static IReadOnlyDictionary<string, object?>? FindOptional(
        OrganizationRelationshipRecordView records,
        OrganizationRelationshipRecordKind kind,
        string? id) => id is null ? null : records.Find(kind, id);
}

/// <summary>限定当前组织关系解析所需的三类 Organization 自有记录。</summary>
internal enum OrganizationRelationshipRecordKind
{
    Company,
    Department,
    Person
}

/// <summary>为同一关系解析提供 published 与 transaction 两种内部读取语义。</summary>
internal abstract class OrganizationRelationshipRecordView(OrganizationSnapshot snapshot)
{
    // 读取指定记录；具体 adapter 决定缺失是抛出、返回 null，还是由 overlay 覆盖。
    internal abstract IReadOnlyDictionary<string, object?>? Find(OrganizationRelationshipRecordKind kind, string id);

    // 三类关系记录固定映射到同一次 Organization 快照中的不可变集合。
    protected IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Records(OrganizationRelationshipRecordKind kind) => kind switch
    {
        OrganizationRelationshipRecordKind.Company => snapshot.Companies,
        OrganizationRelationshipRecordKind.Department => snapshot.Departments,
        OrganizationRelationshipRecordKind.Person => snapshot.Persons,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // overlay 使用平台模型键；映射保持在 Organization 内部，不扩大公共查询 interface。
    protected static string ModelKey(OrganizationRelationshipRecordKind kind) => kind switch
    {
        OrganizationRelationshipRecordKind.Company => "Organization/Company",
        OrganizationRelationshipRecordKind.Department => "Organization/Department",
        OrganizationRelationshipRecordKind.Person => "Organization/Person",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

/// <summary>发布快照已验证引用完整性，缺失的非空关系继续以索引异常失败。</summary>
internal sealed class PublishedOrganizationRelationshipRecordView(OrganizationSnapshot snapshot)
    : OrganizationRelationshipRecordView(snapshot)
{
    // 使用索引器保留 published 查询对损坏引用的严格失败语义。
    internal override IReadOnlyDictionary<string, object?> Find(OrganizationRelationshipRecordKind kind, string id) =>
        Records(kind)[id];
}

/// <summary>事务视图把本次最终变更叠加到发布前快照，并保留显式删除。</summary>
internal sealed class TransactionOrganizationRelationshipRecordView(
    OrganizationSnapshot snapshot,
    IReadOnlyDictionary<(string ModelKey, string Id), IReadOnlyDictionary<string, object?>?>? overlay)
    : OrganizationRelationshipRecordView(snapshot)
{
    // overlay 命中时直接返回其值，包括表示删除的 null；未命中才回落发布前快照。
    internal override IReadOnlyDictionary<string, object?>? Find(OrganizationRelationshipRecordKind kind, string id) =>
        overlay is not null && overlay.TryGetValue((ModelKey(kind), id), out var row)
            ? row
            : Records(kind).GetValueOrDefault(id);
}
