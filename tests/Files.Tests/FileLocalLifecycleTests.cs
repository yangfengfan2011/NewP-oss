using System.Security.Cryptography;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Storage;
using Xunit;
using Microsoft.Extensions.Configuration;

namespace ICP.Platform.Tests.Files;

public sealed class FileLocalLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NewP-Files-Local", Guid.NewGuid().ToString("N"));
    private const string Key = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task NonSeekableSource_IsReadOnce_HashedAndCountedWithoutLengthOrPosition()
    {
        var bytes = Enumerable.Range(0, 130001).Select(value => (byte)(value % 251)).ToArray();
        using var source = new NonSeekableStream(bytes);
        var store = new LocalStorageProvider(_root);
        var result = await store.WritePartialAsync(Key, source, bytes.Length, default);
        Assert.Equal(bytes.Length, result.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal(bytes.Length, source.BytesRead);
        Assert.False(File.Exists(Path.Combine(_root, Key)));
        await store.CompleteAsync(Key, default);
        Assert.False(File.Exists(Path.Combine(_root, Key + ".part")));
        await using var opened = await store.OpenAsync(Key, default);
        using var downloaded = new MemoryStream();
        await opened.CopyToAsync(downloaded);
        Assert.Equal(bytes, downloaded.ToArray());
    }

    [Theory]
    [InlineData(104857600L, true)]
    [InlineData(104857601L, false)]
    public async Task RealOneHundredMiB_IsAccepted_OneExtraByteIs413WithoutFinalContent(long length, bool allowed)
    {
        var store = new LocalStorageProvider(_root);
        using var source = new RepeatingStream(length);
        if (allowed)
        {
            var result = await store.WritePartialAsync(Key, source, 104857600, default);
            Assert.Equal(length, result.Length);
            Assert.Equal(length, source.BytesRead);
            Assert.Equal(64, result.Sha256.Length);
            await store.CompleteAsync(Key, default);
            Assert.Equal(length, new FileInfo(Path.Combine(_root, Key)).Length);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<FileLifecycleException>(() => store.WritePartialAsync(Key, source, 104857600, default));
            Assert.Equal(413, exception.StatusCode);
            Assert.Equal("Files.UploadTooLarge", exception.Code);
            Assert.False(File.Exists(Path.Combine(_root, Key)));
            await store.DeleteContentAsync(Key, default);
            Assert.False(File.Exists(Path.Combine(_root, Key + ".part")));
        }
    }

    [Fact]
    public async Task EmptyContentIs400_AndPartCanBeIdempotentlyDeleted()
    {
        var store = new LocalStorageProvider(_root);
        var exception = await Assert.ThrowsAsync<FileLifecycleException>(() => store.WritePartialAsync(Key, Stream.Null, 100, default));
        Assert.Equal("Files.EmptyContent", exception.Code);
        Assert.Equal(400, exception.StatusCode);
        await store.DeleteContentAsync(Key, default);
        await store.DeleteContentAsync(Key, default);
        Assert.Empty(Directory.EnumerateFiles(_root));
    }

    [Fact]
    public async Task CreateNewAndCompleteNeverOverwriteExistingContent()
    {
        var store = new LocalStorageProvider(_root);
        await store.WritePartialAsync(Key, new MemoryStream([1, 2, 3]), 100, default);
        await Assert.ThrowsAsync<IOException>(() => store.WritePartialAsync(Key, new MemoryStream([9]), 100, default));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(_root, Key + ".part")));
        await store.CompleteAsync(Key, default);
        await store.WritePartialAsync(Key, new MemoryStream([9]), 100, default);
        await Assert.ThrowsAsync<IOException>(() => store.CompleteAsync(Key, default));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(_root, Key)));
    }

    [Fact]
    public async Task WriterAbort_CancelsSource_AndCleanupWaitsForGateExit()
    {
        var gates = new FileIoGate();
        using var writer = gates.ReserveWriter(Key, default);
        using var held = await gates.EnterAsync(Key, writer.Token);
        Assert.True(gates.HasWriter(Key));
        var waiting = gates.EnterAsync(Key, default).AsTask();
        Assert.False(waiting.IsCompleted);
        gates.Abort(Key);
        Assert.True(writer.Token.IsCancellationRequested);
        Assert.False(waiting.IsCompleted);
        held.Dispose();
        using var cleanup = await waiting;
        writer.Dispose();
        Assert.False(gates.HasWriter(Key));
    }

    [Fact]
    public void MetadataProvider_ProjectsThreeFilesEntitiesAndOwnerConstraints()
    {
        var definitions = new FilesModelMetadataProvider().GetDefinitions();
        Assert.Equal(new[] { "FileRecord", "FileUploadLease", "FileUsage" }, definitions.Select(model => model.LogicalKey));
        Assert.All(definitions, definition => Assert.Equal("Files", definition.OwnerModuleId));
        Assert.All(definitions, definition => Assert.Contains(definition.Fields, field => field.Name == "CreatedBy"));
    }

    [Theory]
    [InlineData("", 104857600, 24, 5, 100, 128)]
    [InlineData("relative/root", 104857600, 24, 5, 100, 128)]
    [InlineData(null, 0, 24, 5, 100, 128)]
    [InlineData(null, 104857600, 0, 5, 100, 128)]
    [InlineData(null, 104857600, 24, 0, 100, 128)]
    [InlineData(null, 104857600, 24, 5, 0, 128)]
    [InlineData(null, 104857600, 24, 5, 100, 0)]
    [InlineData(null, 104857600, 24, 5, 100, 1025)]
    public void OptionsRejectInvalidRootAndEveryInvalidLimit(string? root, long max, int hours, int minutes, int batch, int read)
    {
        var result = new FilesOptionsValidator().Validate(null, new() { StorageRoot = root ?? _root, MaxUploadSizeBytes = max,
            UploadLeaseHours = hours, CleanupIntervalMinutes = minutes, CleanupBatchSize = batch, MaxReadBatchSize = read });
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(71582, true)]
    [InlineData(71583, false)]
    public void OptionsRejectOnlyUnrepresentableTimerPeriod(int minutes, bool valid)
    {
        var result = new FilesOptionsValidator().Validate(null, new() { StorageRoot = _root, CleanupIntervalMinutes = minutes });
        Assert.Equal(valid, result.Succeeded);
        if (valid) { using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes)); }
        else Assert.Throws<ArgumentOutOfRangeException>(() => new PeriodicTimer(TimeSpan.FromMinutes(minutes)));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void OptionsRejectUnrepresentableLeaseExpiryAndDoNotCreateRoot()
    {
        var result = new FilesOptionsValidator().Validate(null, new() { StorageRoot = _root, UploadLeaseHours = int.MaxValue });
        Assert.True(result.Failed);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("public/private", false)]
    [InlineData("runtime/modules", false)]
    [InlineData("runtime/modules/private", false)]
    [InlineData("private", true)]
    [InlineData("public-private", true)]
    [InlineData("wwwroot-private", true)]
    public void OptionsProtectCurrentHostStaticRootsWithoutRejectingOtherPrivateDirectories(string relative, bool allowed)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:PluginInstallRoot"] = _root }).Build();
        var result = new FilesOptionsValidator(configuration: configuration).Validate(null, new() { StorageRoot = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)) });
        Assert.Equal(allowed, result.Succeeded);
        Assert.False(Directory.Exists(_root));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private int _offset;
        public int BytesRead => _offset;
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new InvalidOperationException("Length is forbidden.");
        public override long Position { get => throw new InvalidOperationException("Position is forbidden."); set => throw new InvalidOperationException("Position is forbidden."); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, bytes.Length - _offset);
            bytes.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RepeatingStream(long length) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new InvalidOperationException("Length is forbidden.");
        public override long Position { get => throw new InvalidOperationException("Position is forbidden."); set => throw new InvalidOperationException("Position is forbidden."); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, length - BytesRead);
            buffer.Span[..count].Fill(42);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
