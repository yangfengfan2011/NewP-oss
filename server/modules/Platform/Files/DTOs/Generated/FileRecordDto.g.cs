#nullable enable annotations

namespace ICP.Platform.Modules.Files.DTOs;

public sealed class FileRecordDto
{
    public string Id { get; init; }
    public string CreatedBy { get; init; }
    public string? CreatedBy_DisplayValue { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedAt_DisplayValue { get; init; }
    public string UpdatedBy { get; init; }
    public string? UpdatedBy_DisplayValue { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? UpdatedAt_DisplayValue { get; init; }
    public string StorageProviderKey { get; init; }
    public string StorageKey { get; init; }
    public string FileName { get; init; }
    public string ContentType { get; init; }
    public long? Length { get; init; }
    public string? Sha256 { get; init; }
    public string State { get; init; }
}
