using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>一次对象写的互斥来源；不进入可由Before扩展修改的业务Values。</summary>
public abstract record ObjectOperationWriteSource
{
    // 来源由服务端实际调用显式选择，不能从缺失HTTP身份推断，也不携带允许结果。
    private ObjectOperationWriteSource() { }
    /// <summary>尚未迁移的正式声明入口或固定内部写绑定。</summary>
    public sealed record Declared(DataExecutionWriteContext Context) : ObjectOperationWriteSource;
    /// <summary>正式按钮目标；不携带已允许结果，也不继承其他对象的入口许可。</summary>
    public sealed record Button(ButtonExecutionContext Context) : ObjectOperationWriteSource;
    /// <summary>同一真实事务内的显式内部组合；只固定目标与动作，不声明权限下限或继承外层允许。</summary>
    public sealed record Internal(string ResourceKey, string ObjectName, ObjectOperationAction Action, string? ObjectId) : ObjectOperationWriteSource;
}
