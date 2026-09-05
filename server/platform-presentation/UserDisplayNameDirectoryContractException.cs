namespace ICP.Platform.Presentation;

/// <summary>账号名称映射违反结构契约；不能按非关键目录不可用降级。</summary>
public sealed class UserDisplayNameDirectoryContractException() : InvalidOperationException("AUDIT_NAME_DIRECTORY_CONTRACT_INVALID");
