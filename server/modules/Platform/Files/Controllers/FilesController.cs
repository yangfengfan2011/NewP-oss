using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Files.Abstractions;
using ICP.Platform.Modules.Files.Dto;
using ICP.Platform.Modules.Files.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Files.Controllers;

[ApiController]
[Route("api/files")]
[DenyAll("Files product endpoints remain frozen in the current batch.")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
    }

    /// <summary>
    /// 上传文件
    /// </summary>
    [HttpPost("upload")]
    public async Task<ActionResult<UploadResponse>> Upload(
        IFormFile file,
        [FromForm] string? entityType = null,
        [FromForm] string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("文件不能为空");
        }

        using var stream = file.OpenReadStream();

        var request = new UploadRequest
        {
            FileStream = stream,
            FileName = file.FileName,
            ContentType = file.ContentType,
            EntityType = entityType,
            EntityId = entityId,
            UploadedBy = User.Identity?.Name
        };

        var metadata = await _fileService.UploadAsync(request, cancellationToken);

        return Ok(new UploadResponse
        {
            FileId = metadata.FileId,
            FileName = metadata.FileName,
            FileSize = metadata.FileSize,
            ContentType = metadata.ContentType,
            UploadedAt = metadata.UploadedAt
        });
    }

    /// <summary>
    /// 下载文件
    /// </summary>
    [HttpGet("{fileId}/download")]
    public async Task<IActionResult> Download(string fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _fileService.DownloadAsync(fileId, cancellationToken);
            return File(result.FileStream, result.ContentType, result.FileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound($"文件不存在: {fileId}");
        }
    }

    /// <summary>
    /// 获取文件信息
    /// </summary>
    [HttpGet("{fileId}")]
    public async Task<ActionResult<FileInfoResponse>> GetFileInfo(string fileId, CancellationToken cancellationToken = default)
    {
        var metadata = await _fileService.GetMetadataAsync(fileId, cancellationToken);

        if (metadata == null)
        {
            return NotFound($"文件不存在: {fileId}");
        }

        return Ok(new FileInfoResponse
        {
            FileId = metadata.FileId,
            FileName = metadata.FileName,
            FileSize = metadata.FileSize,
            ContentType = metadata.ContentType,
            UploadedBy = metadata.UploadedBy,
            UploadedAt = metadata.UploadedAt,
            EntityType = metadata.EntityType,
            EntityId = metadata.EntityId,
            IsDeleted = metadata.IsDeleted
        });
    }

    /// <summary>
    /// 查询文件列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FileInfoResponse>>> Query(
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null,
        [FromQuery] bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var files = await _fileService.QueryAsync(entityType, entityId, includeDeleted, cancellationToken);

        var response = files.Select(f => new FileInfoResponse
        {
            FileId = f.FileId,
            FileName = f.FileName,
            FileSize = f.FileSize,
            ContentType = f.ContentType,
            UploadedBy = f.UploadedBy,
            UploadedAt = f.UploadedAt,
            EntityType = f.EntityType,
            EntityId = f.EntityId,
            IsDeleted = f.IsDeleted
        }).ToList();

        return Ok(response);
    }

    /// <summary>
    /// 删除文件（软删除）
    /// </summary>
    [HttpDelete("{fileId}")]
    public async Task<IActionResult> Delete(string fileId, CancellationToken cancellationToken = default)
    {
        var deleted = await _fileService.DeleteAsync(fileId, User.Identity?.Name, cancellationToken);

        if (!deleted)
        {
            return NotFound($"文件不存在: {fileId}");
        }

        return NoContent();
    }

    /// <summary>
    /// 物理删除文件
    /// </summary>
    [HttpDelete("{fileId}/permanent")]
    public async Task<IActionResult> PermanentDelete(string fileId, CancellationToken cancellationToken = default)
    {
        var deleted = await _fileService.PermanentDeleteAsync(fileId, cancellationToken);

        if (!deleted)
        {
            return NotFound($"文件不存在: {fileId}");
        }

        return NoContent();
    }
}
