using ICP.Platform.Modules.Files.Storage;
using Xunit;

namespace ICP.Platform.Tests.Files;

public class LocalStorageProviderTests
{
    private readonly string _testBasePath;

    public LocalStorageProviderTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), "FileTests", Guid.NewGuid().ToString());
    }

    [Fact]
    public async Task SaveAsync_ShouldSaveFileToLocalStorage()
    {
        // Arrange
        var provider = new LocalStorageProvider(_testBasePath);
        var content = "test content"u8.ToArray();
        using var stream = new MemoryStream(content);

        // Act
        var storagePath = await provider.SaveAsync(stream, "test.txt");

        // Assert
        Assert.NotEmpty(storagePath);
        var fullPath = Path.Combine(_testBasePath, storagePath);
        Assert.True(File.Exists(fullPath));

        var savedContent = await File.ReadAllBytesAsync(fullPath);
        Assert.Equal(content, savedContent);

        // Cleanup
        Directory.Delete(_testBasePath, true);
    }

    [Fact]
    public async Task ReadAsync_ShouldReadFileFromLocalStorage()
    {
        // Arrange
        var provider = new LocalStorageProvider(_testBasePath);
        var content = "test content"u8.ToArray();
        using var saveStream = new MemoryStream(content);
        var storagePath = await provider.SaveAsync(saveStream, "test.txt");

        // Act
        byte[] readContent;
        using (var readStream = await provider.ReadAsync(storagePath))
        {
            using var ms = new MemoryStream();
            await readStream.CopyToAsync(ms);
            readContent = ms.ToArray();
        }

        // Assert
        Assert.Equal(content, readContent);

        // Cleanup
        Directory.Delete(_testBasePath, true);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteFileFromLocalStorage()
    {
        // Arrange
        var provider = new LocalStorageProvider(_testBasePath);
        var content = "test content"u8.ToArray();
        using var stream = new MemoryStream(content);
        var storagePath = await provider.SaveAsync(stream, "test.txt");

        // Act
        await provider.DeleteAsync(storagePath);

        // Assert
        var fullPath = Path.Combine(_testBasePath, storagePath);
        Assert.False(File.Exists(fullPath));

        // Cleanup
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrueForExistingFile()
    {
        // Arrange
        var provider = new LocalStorageProvider(_testBasePath);
        var content = "test content"u8.ToArray();
        using var stream = new MemoryStream(content);
        var storagePath = await provider.SaveAsync(stream, "test.txt");

        // Act
        var exists = await provider.ExistsAsync(storagePath);

        // Assert
        Assert.True(exists);

        // Cleanup
        Directory.Delete(_testBasePath, true);
    }
}
