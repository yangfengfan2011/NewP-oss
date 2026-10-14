using System.Text.Json.Serialization;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Files.Contracts;

namespace ICP.Platform.Modules.Document.Contracts;

/// <summary>显式带文件创建选择；技术身份、角色和次序由 Document 生成。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentUploadSet
{
    public required string MainUploadId { get; init; }
    public IReadOnlyList<string>? AttachmentUploadIds { get; init; }
}

/// <summary>Document 拥有完整文件创建、读取和下载，Workspace 不访问其私有表。</summary>
public interface IDocumentFiles
{
    /// <summary>复用正式字段与候选 create 规则预检；只返回失败信息，不登记对象或授权凭证。</summary>
    Task<StandardCrudError?> ValidateUploadAsync(IReadOnlyDictionary<string, object?> values, string fileName,
        string contentType, CancellationToken cancellationToken = default);

    /// <summary>在当前对象事务内经原 Create 建立首版、版次与真实文件关联；省略选择保留 metadata-only 行为。</summary>
    Task<StandardCrudMutationResult> CreateWithFilesAsync(IReadOnlyDictionary<string, object?> values,
        DocumentUploadSet? uploads, CancellationToken cancellationToken = default);

    /// <summary>沿实际当前身份与 owner read 获取有界安全列表；不会公开存储身份或路径。</summary>
    Task<DocumentFileList> ReadAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>重新裁决 Document read/download 并从真实 Link 派生使用引用后打开内容。</summary>
    Task<FileUsageContent> OpenAsync(string documentId, string fileLinkId, CancellationToken cancellationToken = default);
}

/// <summary>当前 regular 文件列表只公开业务 Link 与安全元数据。</summary>
public sealed record DocumentFileList(string DocumentId, string VersionId, string IterationId, bool DownloadAllowed,
    IReadOnlyList<DocumentFileItem> Files);

/// <summary>角色与次序属于 Document；下载定位实际 Link，底层 File/Usage 不进入 HTTP。</summary>
public sealed record DocumentFileItem(string FileLinkId, string Role, long Ordinal, string FileName, long Length, string ContentType);
