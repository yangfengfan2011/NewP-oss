using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Document;

/// <summary>详情只交接真实目标与当前读取位置；文件列表由所属 API 和独立查询运行时装载。</summary>
public sealed class DocumentFilesSummaryProvider(IHttpContextAccessor http) : IStandardCrudSummaryProvider
{
    public string ResourceKey => "document.document";
    public string Key => "files";

    /// <inheritdoc/>
    public Task<StandardCrudSummaryBlock> BuildAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default) => Task.FromResult(new StandardCrudSummaryBlock
        {
            Status = "ready", Data = new { documentId = (string)detail["EID"]!, featurePosition = http.HttpContext?.Request.Headers["X-NewP-Feature"].ToString() }
        });
}
