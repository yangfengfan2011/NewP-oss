using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>Folder拥有者解析当前名称；普通履历只传入已通过read及空间范围的对象。</summary>
public sealed class WorkspaceFolderAuditNames(EfPlatformDbContext db, WorkspaceFolderReadDescriptor descriptor) : IBusinessAuditNameDirectory
{
    public string Kind => "workspace.folder";

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken = default)
    {
        var resource = descriptor.Value;
        return await db.Set<Dictionary<string, object?>>(resource.ModelName).AsNoTracking()
            .Where(row => ids.Contains(EF.Property<string>(row, resource.IdField)))
            .Select(row => new { Id = EF.Property<string>(row, resource.IdField), Name = EF.Property<string>(row, resource.DisplayField) })
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
    }
}
