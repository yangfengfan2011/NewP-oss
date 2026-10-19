using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>一次对象写的互斥来源；不进入可由Before扩展修改的业务Values。</summary>
public abstract record ObjectOperationWriteSource
{
    // 来源只能是下面两种显式形状，调用方不能扩展新的许可标记。
    private ObjectOperationWriteSource() { }
    /// <summary>已迁移的正式入口或固定内部写绑定。</summary>
    public sealed record Declared(DataExecutionWriteContext Context) : ObjectOperationWriteSource;
    /// <summary>尚未迁移的具体调用者；只匹配owner逐动作登记的过渡项，#321全部退役。</summary>
    public sealed record Legacy(Type Caller, string Member, string ResourceKey, string Operation, string? ObjectId) : ObjectOperationWriteSource;
}

/// <summary>对象owner只为尚未迁移的既有调用位置保留具体动作，不能豁免未知调用者。</summary>
public sealed record ObjectOperationLegacyBinding(string Caller, string Operation);
