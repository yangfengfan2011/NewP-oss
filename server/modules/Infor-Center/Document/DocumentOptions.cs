namespace ICP.Platform.Modules.Document;

/// <summary>图文档 generic 用途的输入边界，不属于平台文件存储政策。</summary>
public sealed class DocumentOptions
{
    public string[] AllowedFileExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx"];
    public int MaxAttachmentsPerDocument { get; set; } = 100;
}
