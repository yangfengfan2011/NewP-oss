using ICP.Platform.Crud.Descriptors;
namespace ICP.Platform.Crud.Abstractions;
/// <summary>资源拥有者在统一创建事务中采集规范业务字段。</summary>
public interface IStandardCrudCreationRecorder
{
    string ResourceKey { get; }
    /// <summary>登记已生成主键及规范字段的创建事实，不自行保存。</summary>
    ValueTask RegisterAsync(IReadOnlyDictionary<string, object?> created, CancellationToken cancellationToken = default);
}
/// <summary>标准仓储的受控创建参与接口，与原立即保存协议分开。</summary>
public interface IStandardCrudCreationParticipant
{
    /// <summary>在当前事务内只追踪新行，数据库保存归属外层事务执行器。</summary>
    IReadOnlyDictionary<string, object?> RegisterCreate(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values);
}
