using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Files.Contracts;

namespace ICP.Platform.Modules.Document;

/// <summary>真实Document复制阶段选定固定来源；Workspace和Files不能提供允许结果。</summary>
internal static class DocumentCopyExecutions
{
    // 原owner读取和复制裁决各在原位置消费，不把快照或已有行当成read允许证明。
    internal static DataExecutionInternalBinding Source() => new("document.document.copy.source", "document.backend", typeof(DocumentFiles), nameof(DocumentFiles.ReadCopySourceAsync));

    // 文件元数据原批read独立履行；此来源不会再次要求已完成的documentObjectCopy。
    internal static FileOwnerInternalExecutionContext Metadata(string documentId) => new(
        new("document.files.copyMetadata", "document.backend", typeof(DocumentFiles), nameof(DocumentFiles.ReadCopySourceAsync)), "document.document", documentId);

    // 两个真实文件复制阶段各自交接内部来源与所属Document，policy仍从文件关联独立回链。
    internal static FileOwnerInternalExecutionContext Files(string documentId, string member) => new(
        new("document.files.copySource", "document.files.copySource", typeof(DocumentFiles), member), "document.document", documentId);

}
