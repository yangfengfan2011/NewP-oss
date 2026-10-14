using ICP.Platform.Modules.Files.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>请求按真实字节限额，始终暴露非 seek 流；只释放包装器，不抢请求体所有权。</summary>
internal sealed class WorkspaceUploadRequestStream(Stream source, long limit) : Stream
{
    public long BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Count(source.Read(buffer, offset, count));

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await source.ReadAsync(buffer, cancellationToken));

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Count(await source.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    // 计数覆盖 chunked 请求，未知 Content-Length 也不能绕过入口预算。
    private int Count(int count)
    {
        BytesRead += count;
        if (BytesRead > limit) throw new FileLifecycleException(413, "Files.UploadTooLarge");
        return count;
    }

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
