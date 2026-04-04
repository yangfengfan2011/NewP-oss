using ICP.Platform.Modules.Files.Abstractions;
using ICP.Platform.Modules.Files.Models;
using ICP.Platform.Modules.Files.Services;
using Moq;
using Xunit;

namespace ICP.Platform.Tests.Files;

public class FileServiceTests
{
    [Fact]
    public async Task UploadAsync_ShouldSaveFileAndMetadata()
    {
        // Arrange
        var mockStorageProvider = new Mock<IStorageProvider>();
        var mockMetadataRepository = new Mock<IFileMetadataRepository>();

        mockStorageProvider.Setup(x => x.ProviderName).Returns("Test");
        mockStorageProvider
            .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test/path/file.txt");

        var fileService = new FileService(mockStorageProvider.Object, mockMetadataRepository.Object);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var request = new UploadRequest
        {
            FileStream = stream,
            FileName = "test.txt",
            ContentType = "text/plain",
            UploadedBy = "testuser"
        };

        // Act
        var result = await fileService.UploadAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.FileId);
        Assert.Equal("test.txt", result.FileName);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal("testuser", result.UploadedBy);

        mockStorageProvider.Verify(
            x => x.SaveAsync(It.IsAny<Stream>(), "test.txt", It.IsAny<CancellationToken>()),
            Times.Once
        );

        mockMetadataRepository.Verify(
            x => x.SaveAsync(It.IsAny<FileMetadata>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task DownloadAsync_ShouldReturnFileStream()
    {
        // Arrange
        var mockStorageProvider = new Mock<IStorageProvider>();
        var mockMetadataRepository = new Mock<IFileMetadataRepository>();

        var metadata = new FileMetadata
        {
            FileId = "test-id",
            FileName = "test.txt",
            FileSize = 100,
            ContentType = "text/plain",
            StoragePath = "test/path/file.txt",
            StorageProvider = "Test",
            UploadedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        mockMetadataRepository
            .Setup(x => x.GetByIdAsync("test-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        var fileStream = new MemoryStream(new byte[] { 1, 2, 3 });
        mockStorageProvider
            .Setup(x => x.ReadAsync("test/path/file.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileStream);

        var fileService = new FileService(mockStorageProvider.Object, mockMetadataRepository.Object);

        // Act
        var result = await fileService.DownloadAsync("test-id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test.txt", result.FileName);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(fileStream, result.FileStream);
    }

    [Fact]
    public async Task DeleteAsync_ShouldMarkFileAsDeleted()
    {
        // Arrange
        var mockStorageProvider = new Mock<IStorageProvider>();
        var mockMetadataRepository = new Mock<IFileMetadataRepository>();

        var metadata = new FileMetadata
        {
            FileId = "test-id",
            FileName = "test.txt",
            FileSize = 100,
            ContentType = "text/plain",
            StoragePath = "test/path/file.txt",
            StorageProvider = "Test",
            UploadedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        mockMetadataRepository
            .Setup(x => x.GetByIdAsync("test-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        var fileService = new FileService(mockStorageProvider.Object, mockMetadataRepository.Object);

        // Act
        var result = await fileService.DeleteAsync("test-id", "testuser");

        // Assert
        Assert.True(result);
        mockMetadataRepository.Verify(
            x => x.MarkAsDeletedAsync("test-id", "testuser", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }
}
