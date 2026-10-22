namespace ICP.Platform.Crud.Models;

/// <summary>
/// 表示同步标准 CRUD 导出超过平台配置的行数上限。
/// </summary>
public sealed class StandardCrudExportLimitExceededException : Exception
{
    /// <summary>
    /// 使用稳定的可理解消息创建导出上限异常。
    /// </summary>
    public StandardCrudExportLimitExceededException(int maxRows)
        : base($"筛选结果超过同步导出上限 {maxRows} 行，请缩小筛选范围后重试。")
    {
        MaxRows = maxRows;
    }

    /// <summary>
    /// 获取当前平台允许的同步导出最大行数。
    /// </summary>
    public int MaxRows { get; }
}
