namespace ICP.Platform.Crud.Models;

/// <summary>
/// 表示仓储已确认的数据库约束竞争；服务边界可将其转换为稳定公共冲突，而未知持久化故障继续传播。
/// </summary>
internal sealed class StandardCrudPersistenceConflictException : Exception
{
    /// <summary>
    /// 保留原始 provider 异常供服务端诊断，但不将其细节暴露给公共响应。
    /// </summary>
    public StandardCrudPersistenceConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
