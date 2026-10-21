using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Files.Contracts;

/// <summary>业务拥有者选定的正式执行来源；文件引用不能充当授权目标。</summary>
public abstract record FileOwnerOperationContext;
/// <summary>候选创建后的实际新对象；首次确认还必须匹配owner同事务的真实Added关联证明。</summary>
public sealed record FileOwnerCreationContext(DataExecutionWriteContext Source, string ObjectId) : FileOwnerOperationContext;
/// <summary>原删除流程选定的真实对象；policy在自己的锁内读取阶段独立执行固定Release要求。</summary>
public sealed record FileOwnerReleaseContext(string ResourceKey, string ObjectId) : FileOwnerOperationContext;
/// <summary>由业务owner选定的正式读取/下载执行、资源与真实对象；Files不接受客户端关联替代目标。</summary>
public sealed record FileOwnerExecutionContext(DataExecutionBindingReference Binding, string ResourceKey, string ObjectId) : FileOwnerOperationContext;
/// <summary>业务owner固定的内部执行、资源与真实所属对象；不携带权限结果或客户端功能头。</summary>
public sealed record FileOwnerInternalExecutionContext(DataExecutionInternalBinding Source, string ResourceKey, string ObjectId) : FileOwnerOperationContext;

/// <summary>业务拥有者从实际关联生成的身份；HTTP 客户端不能提供它。</summary>
public sealed record FileOwnerReference(string Key, string Id);
/// <summary>业务真实关联派生的使用引用，三个身份必须双向一致。</summary>
public sealed record FileUsageReference(string UsageId, string FileId, FileOwnerReference Owner);
/// <summary>已确认暂存成功的安全结果，不含物理路径或存储键。</summary>
public sealed record StagedFileInfo(string UploadId, string FileId, string FileName, long Length, string ContentType, DateTime ExpiresAt);
public sealed record ConfirmedFileUsage(string UsageId, string FileId);
public sealed record FileUsageMetadata(string UsageId, string FileId, string FileName, long Length, string ContentType);
/// <summary>调用方拥有返回流，必须在请求完成或取消时释放它。</summary>
public sealed record FileUsageContent(Stream FileStream, string FileName, string ContentType, long Length);

/// <summary>独立上传及业务事务内使用登记的稳定消费面。</summary>
public interface IFileLifecycleService
{
    /// <summary>暂存集合内部每批查询及安全元数据单次输入的上限，不是暂存集合总量上限。</summary>
    int MaxReadBatchSize { get; }
    long MaxUploadSizeBytes { get; }
    /// <summary>先持久接收意图再流式写入，actor 取自可信服务端上下文。</summary>
    Task<StagedFileInfo> StageAsync(Stream stream, string fileName, string? contentType, string ownerKey, CancellationToken token = default);
    /// <summary>
    /// 按当前 actor 和登记用途读取完整暂存集合，内部按 MaxReadBatchSize 分批，按输入次序返回。
    /// 空集合仍核验 actor 与用途；空白或重复身份拒绝。整集合身份不可见优先于本人过期、取消或已消费冲突。
    /// 集合总量由业务 owner 约束，结果占用 O(N) 空间；此读取不预留使用，Confirm 仍须在原业务事务内重验。
    /// </summary>
    Task<IReadOnlyList<StagedFileInfo>> ReadOwnedStagedAsync(IReadOnlyList<string> uploadIds, string ownerKey, CancellationToken token = default);
    /// <summary>在已有平台事务内登记使用；owner核对其typed来源、真实创建状态与Added关联，不会保存、Flush或独立提交。</summary>
    Task<ConfirmedFileUsage> ConfirmUsageAsync(string uploadId, FileOwnerReference owner, FileOwnerOperationContext context, CancellationToken token = default);
    /// <summary>有界批量验证 owner read 与完整持久回链，按输入次序返回安全元数据。</summary>
    Task<IReadOnlyList<FileUsageMetadata>> ReadUsageMetadataAsync(IReadOnlyList<FileUsageReference> usageReferences, FileOwnerOperationContext context, CancellationToken token = default);
    /// <summary>由owner独立核验typed来源、read/download及持久使用回链后开流；缺少物理内容是完整性故障。</summary>
    Task<FileUsageContent> OpenUsageAsync(FileUsageReference usageReference, FileOwnerOperationContext context, CancellationToken token = default);
    /// <summary>只取消当前 actor 的未确认暂存；已确认使用不能通过此操作删除。</summary>
    Task CancelStageAsync(string uploadId, CancellationToken token = default);
    /// <summary>在已有业务事务内标记释放和回收候选，不保存或删除字节。</summary>
    Task ReleaseUsageAsync(FileUsageReference usageReference, FileOwnerReleaseContext context, CancellationToken token = default);
}

public enum FileOwnerOperation { Confirm, Read, Download, Release, CopySource, ConfirmCopy }

/// <summary>用途拥有者贡献实际业务资格及关联验证，Files 不认识业务类型。</summary>
public interface IFileOwnerPolicy
{
    string OwnerKey { get; }
    /// <summary>只验证所属用途的格式规则；安全名和上传上限由 Files 执行。</summary>
    void ValidateUpload(string safeFileName, string contentType);
    /// <summary>首次 Confirm 的 usageId 为空，必须证明本次创建与真实 Added 关联；其余操作核原使用。</summary>
    Task VerifyAsync(FileOwnerReference owner, string fileId, string? usageId, FileOwnerOperation operation, FileOwnerOperationContext? context, CancellationToken token);
    /// <summary>同用途批量核验实际业务回链及资格，避免逐使用重复查询。</summary>
    Task VerifyBatchAsync(IReadOnlyList<FileUsageReference> references, FileOwnerOperation operation, FileOwnerOperationContext? context, CancellationToken token);
}

/// <summary>活跃用途显式声明必需 policy；启动时缺失或重复登记失败关闭。</summary>
public sealed record FileOwnerPolicyRequirement(string OwnerKey);

/// <summary>可安全翻译到 HTTP 的稳定文件错误；持久事实损坏不使用此类型降级。</summary>
public sealed class FileLifecycleException : Exception
{
    /// <summary>记录安全状态、原因与可选字段，不携带私有存储信息。</summary>
    public FileLifecycleException(int statusCode, string code, string? field = null) : base(code)
    {
        StatusCode = statusCode;
        Code = code;
        Field = field;
    }
    public int StatusCode { get; }
    public string Code { get; }
    public string ReasonKey => Code;
    public string? Field { get; }
}
