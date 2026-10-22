namespace ICP.Platform.Modules.Files.Storage;

/// <summary>只操作持久意图已登记的私有键；业务和事务留在生命周期服务。</summary>
public interface IFileContentStore
{
    /// <summary>使用 CreateNew 写 part，单遍计算真实长度和 SHA256；不读取输入的 Length/Position。</summary>
    Task<FileContentMeasurement> WritePartialAsync(string key, Stream source, long maximumBytes, CancellationToken token);
    /// <summary>所有写句柄关闭后将 part 原子改名为不可覆盖的正式内容。</summary>
    Task CompleteAsync(string key, CancellationToken token);
    /// <summary>打开私有正式内容，调用方拥有返回流。</summary>
    Task<Stream> OpenAsync(string key, CancellationToken token);
    /// <summary>幂等删除该登记键的 part 和正式内容。</summary>
    Task DeleteContentAsync(string key, CancellationToken token);
}

public sealed record FileContentMeasurement(long Length, string Sha256);
