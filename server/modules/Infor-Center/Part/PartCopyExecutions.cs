using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Part;

/// <summary>Part真副本拥有者在真实方法固定内部来源，不接受客户端执行名或允许结果。</summary>
internal static class PartCopyExecutions
{
    internal static DataExecutionInternalBinding Source() =>
        new("part.part.copy.source", "part.backend", typeof(PartCopyBehavior), nameof(PartCopyBehavior.ExecuteAsync));

    internal static DataExecutionWriteContext Create() => new(new("part.part.internal.create", "standardCrud.create", "submit"), "part.part", null)
    { Internal = new("part.part.internal.create", "standardCrud.create", typeof(PartCopyBehavior), nameof(PartCopyBehavior.ExecuteAsync)) };
}