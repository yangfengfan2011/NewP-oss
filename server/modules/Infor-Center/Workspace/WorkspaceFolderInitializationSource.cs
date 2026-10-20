using ICP.Platform.Authorization.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContents
{
    // 只包装本次现有old→new映射及Add返回值；不生成全局凭据或权限允许结果。
    private sealed record FolderStructureCreationFacts(string Root,
        IReadOnlyDictionary<string, Dictionary<string, object?>> Candidates,
        IReadOnlyDictionary<string, EntityEntry<Dictionary<string, object?>>> Entries,
        IDbContextTransaction Transaction);

    // 外部根复用原parent角色，内部父只从本次incoming映射形成真实创建facts，不要求新父可读。
    private DataScopeObjectInitializationSource StructureInitializationSource(DataExecutionBindingReference binding, string oldId,
        IReadOnlyList<string> parents, IReadOnlyList<WorkspaceStructureEdge> incoming, FolderStructureCreationFacts creation)
    {
        var target = CreatedFolderFacts(creation, oldId);
        if (StringComparer.OrdinalIgnoreCase.Equals(oldId, creation.Root))
        {
            if (incoming.Count != 0) throw InvalidInitializationSource();
            return new DataScopeObjectInitializationSource.Declared(WorkspaceFolderCopyExecutions.StructureCaller(), binding, parents, target);
        }
        var actualParents = new List<DataExecutionWriteFacts>(incoming.Count);
        foreach (var edge in incoming)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(edge.Child, oldId)) throw InvalidInitializationSource();
            actualParents.Add(CreatedFolderFacts(creation, edge.Parent));
        }
        if (actualParents.Count == 0 || actualParents.Select(parent => parent.ObjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != actualParents.Count
            || !parents.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(actualParents.Select(parent => parent.ObjectId)))
            throw InvalidInitializationSource();
        return new DataScopeObjectInitializationSource.DeclaredFromCreatedParents(WorkspaceFolderCopyExecutions.StructureCaller(), binding, actualParents, target);
    }

    // 通过直接索引核对当前事务和实际EF行引用，不逐父扫描全图，也不把基础设施对象带出本模块。
    private DataExecutionWriteFacts CreatedFolderFacts(FolderStructureCreationFacts creation, string oldId)
    {
        if (!transactions.IsActive || !ReferenceEquals(creation.Transaction, db.Database.CurrentTransaction)
            || !creation.Candidates.TryGetValue(oldId, out var candidate) || !creation.Entries.TryGetValue(oldId, out var entry)
            || !ReferenceEquals(entry.Context, db) || !ReferenceEquals(entry.Entity, candidate)
            || entry.Metadata.Name != WorkspacePositions.FolderModel || entry.State is EntityState.Detached or EntityState.Deleted)
            throw InvalidInitializationSource();
        var idField = entry.Metadata.FindPrimaryKey()!.Properties.Single().Name;
        if (!candidate.TryGetValue(idField, out var key) || key is not string id
            || !Equals(entry.Property(idField).CurrentValue, id)) throw InvalidInitializationSource();
        return new("workspace.folder", id, candidate);
    }

    // 来源不符在任何父配置锁、读取与克隆之前关闭。
    private static ActionDataAccessException InvalidInitializationSource() => new(400, "ActionDataAccess.InitializationSourceInvalid");
}
