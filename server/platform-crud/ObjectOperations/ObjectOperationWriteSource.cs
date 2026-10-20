using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>一次对象写的互斥来源；不进入可由Before扩展修改的业务Values。</summary>
public abstract record ObjectOperationWriteSource
{
    // 来源只能是正式声明形状，调用方不能扩展新的许可标记。
    private ObjectOperationWriteSource() { }
    /// <summary>已迁移的正式入口或固定内部写绑定。</summary>
    public sealed record Declared(DataExecutionWriteContext Context) : ObjectOperationWriteSource;
}
