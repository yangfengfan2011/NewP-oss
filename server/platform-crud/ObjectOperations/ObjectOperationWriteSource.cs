using System.Collections.Frozen;
using ICP.Platform.Crud.Models;
using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>一次对象写的互斥来源；不进入可由Before扩展修改的业务Values。</summary>
public abstract record ObjectOperationWriteSource
{
    // 来源由服务端实际调用显式选择，不能从缺失HTTP身份推断，也不携带允许结果。
    private ObjectOperationWriteSource() { }
    /// <summary>正式按钮目标；不携带已允许结果，也不继承其他对象的入口许可。</summary>
    public sealed record Button(ButtonExecutionContext Context) : ObjectOperationWriteSource;
    /// <summary>同一真实事务内的显式内部组合；只固定目标与动作，不声明权限下限或继承外层允许。</summary>
    public sealed record Internal(string ResourceKey, string ObjectName, ObjectOperationAction Action, string? ObjectId) : ObjectOperationWriteSource;
    /// <summary>真实core持有的一次候选交接；不进入业务输入，不包含允许结果或独立权限要求。</summary>
    public sealed record CreateContinuation : ObjectOperationWriteSource
    {
        private readonly ObjectOperationContext root;
        private readonly ObjectCreationBoundary boundary;
        private readonly Lease lease = new();
        internal string ResourceKey { get; }
        internal string ObjectName { get; }

        // 只有当前真实core能创建；record复制共享一次性lease，不能复制出第二次写入能力。
        internal CreateContinuation(ObjectOperationContext root, string resourceKey, string objectName, ObjectCreationBoundary boundary)
        {
            this.root = root;
            ResourceKey = resourceKey;
            ObjectName = objectName;
            this.boundary = boundary;
        }

        // writer在任何候选准备前重验实际owner；root的有效期覆盖其正常嵌套child core。
        internal void Validate(string resourceKey, string objectName)
        {
            root.EnsureCoreActive();
            if (ResourceKey != resourceKey || ObjectName != objectName)
                throw new ActionDataAccessException(400, "ObjectOperation.CreationBindingInvalid");
        }

        // 候选只冻结一次，外层不能替换它；persist闭包只消费同一规范值且最多一次。
        internal async Task<StandardCrudMutationResult> InvokeAsync(IReadOnlyDictionary<string, object?> candidate,
            Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<StandardCrudMutationResult>> persist, CancellationToken token)
        {
            root.EnsureCoreActive();
            if (Interlocked.CompareExchange(ref lease.Invocations, 1, 0) != 0)
                throw new InvalidOperationException("OBJECT_CREATION_CONTINUATION_REUSED");
            var official = candidate.ToFrozenDictionary(StringComparer.Ordinal);
            var result = await boundary(official, Persist, token);
            if (result.Succeeded && Volatile.Read(ref lease.Writes) != 1)
                throw new InvalidOperationException("OBJECT_CREATION_PERSIST_REQUIRED");
            return result;

            // 有效期与一次性约束在调用时验证，不能保存闭包在离开root后另行写入。
            Task<StandardCrudMutationResult> Persist(CancellationToken cancellationToken)
            {
                root.EnsureCoreActive();
                if (Interlocked.CompareExchange(ref lease.Writes, 1, 0) != 0)
                    throw new InvalidOperationException("OBJECT_CREATION_PERSIST_REUSED");
                return persist(official, cancellationToken);
            }
        }

        private sealed class Lease
        {
            internal int Invocations;
            internal int Writes;
        }
    }
}

/// <summary>完整候选及其唯一持久化续体；服务端业务在自己的保护生命周期内交给统一平台。</summary>
/// <param name="candidate">实际owner补齐的只读候选；不能提供替代候选。</param>
/// <param name="persist">仅持久化该候选一次，离开原core即失效。</param>
/// <param name="cancellationToken">本次操作的取消令牌。</param>
public delegate Task<StandardCrudMutationResult> ObjectCreationBoundary(IReadOnlyDictionary<string, object?> candidate,
    Func<CancellationToken, Task<StandardCrudMutationResult>> persist, CancellationToken cancellationToken);
