namespace ICP.Platform.Modules.Files.Storage;

/// <summary>请求取消时释放真实文件句柄；正常完成仍由返回流的 owner 释放。</summary>
internal sealed class CancellationReadStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationTokenRegistration _registration;
    // 取消注册直接释放句柄，避免失败请求持续阻止 pending 回收。
    public CancellationReadStream(Stream inner, CancellationToken token)
    {
        _inner = inner;
        _registration = token.Register(static value => ((Stream)value!).Dispose(), inner);
    }
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _registration.Dispose(); _inner.Dispose(); }
        base.Dispose(disposing);
    }
    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await _registration.DisposeAsync();
        await _inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
