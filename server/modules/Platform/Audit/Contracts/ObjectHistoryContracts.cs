using System.Text.Json.Serialization;

namespace ICP.Platform.Modules.Audit.Contracts;

/// <summary>普通履历仅接受当前对象范围内的筛选；对象身份由真实owner解析，不属于筛选输入。</summary>
public sealed record ObjectHistoryFilter(int PageIndex = 1, int PageSize = 20, DateTime? From = null, DateTime? To = null,
    string? ActorId = null, string? EventType = null, string? ObjectType = null, string? IpAddress = null, string? Description = null);
/// <summary>输出只复用同一已应用筛选；selected每个记录均须属于当前对象，all不受分页裁剪。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ObjectHistoryOutputRequest(string Mode, ObjectHistoryFilter Filter, IReadOnlyList<string>? SelectedIds = null);
/// <summary>安全列表协议；不包含管理员审计的原始字段、关联标识或追踪信息。</summary>
public sealed record ObjectHistoryPage(IReadOnlyList<ObjectHistoryItem> Items, int Total, int PageIndex, int PageSize);
/// <summary>稳定事件ID只用于本对象记录选择；对象类型是规范模型键，不是关联对象身份。</summary>
public sealed record ObjectHistoryItem(string Id, string EventType, DateTime OccurredAt, string ObjectType,
    ObjectHistoryActor Actor, ObjectHistoryTarget Target, ObjectHistoryDescription Description, string? IpAddress);
/// <summary>当前可读操作者目录；缺失或不可读为unavailable且不回退原账号ID。</summary>
public sealed record ObjectHistoryActor(string? Name, string Status);
/// <summary>名称来源为historical/current/unrecorded/unavailable；不可读或缺失对象不回退历史文本或标识。</summary>
public sealed record ObjectHistoryTarget(string? Name, string? Code, string NameSource);
/// <summary>owner声明的安全本地化摘要；参数只能来自明确许可投影，不传任意字段JSON。</summary>
public sealed record ObjectHistoryDescription(string Key, IReadOnlyDictionary<string, string> Parameters);
/// <summary>既有目标身份的精确编码；不改写旧审计事实。</summary>
public enum ObjectHistoryTargetFormat { ObjectId, QualifiedObjectId, DeclaredFields }
/// <summary>固定BusinessAuditField中的明确前/后事实，不接受任意JSONPath。</summary>
public enum ObjectHistoryFieldSide { Before, After }
/// <summary>owner声明确切字段和事实侧；空Kind使用当前规范模型键，不能把资源键和模型键混同。</summary>
public sealed record ObjectHistoryFieldAssociation(string Field, ObjectHistoryFieldSide Side, string? Kind = null);
/// <summary>事件对象类型来自owner明确声明的文本字段事实，不从当前查询对象类型猜测关系事件。</summary>
public sealed record ObjectHistoryTypeField(string Field, ObjectHistoryFieldSide Side);
/// <summary>固定文本快照只读取After侧的text事实，不推导字段差异或任意路径。</summary>
public sealed record ObjectHistoryTextSnapshot(string NameField, string CodeField);
/// <summary>真实owner的静态事件投影。空RecordResource指查询资源，空TargetResourceKey适用于所有规范资源。</summary>
public sealed record ObjectHistoryEventPolicy(string OwnerModuleId, string EventType, string? RecordResource,
    string? TargetResourceKey, ObjectHistoryTargetFormat TargetFormat, string DescriptionKey)
{
    /// <summary>仅DeclaredFields模式使用已声明字段关联；保留原构造形状供已签发调用方使用。</summary>
    public IReadOnlyList<ObjectHistoryFieldAssociation>? FieldAssociations { get; init; }
    /// <summary>关系事件可按已存ChildModelKey等明确事实投影/筛选类型；无声明使用当前规范目标模型。</summary>
    public ObjectHistoryTypeField? ObjectTypeField { get; init; }
    /// <summary>关系事件的真实主体；先按ObjectType解析规范资源，再验证此字段的Kind与对象读取资格。</summary>
    public ObjectHistoryFieldAssociation? SubjectField { get; init; }
    /// <summary>owner确有登记的名称/编码文本快照；无声明只能使用当前可读目录并明确来源。</summary>
    public ObjectHistoryTextSnapshot? TextSnapshot { get; init; }
}
/// <summary>普通履历共享安全查询；始终完整读取目标，未迁移owner保留原附加检查，失败继续传播。</summary>
public interface IObjectHistoryQuery
{
    /// <summary>调用方先验证正式功能与真实来源；null表示当前对象不可查询，记录过滤与分页在存储侧执行。</summary>
    Task<ObjectHistoryPage?> ListAsync(string resourceKey, string objectId, ObjectHistoryFilter filter,
        CancellationToken cancellationToken = default);
    /// <summary>选中或全部输出仍每次交集对象与筛选；任何选中记录不属于集合时整体返回null，不部分成功。</summary>
    Task<IReadOnlyList<ObjectHistoryItem>?> OutputAsync(string resourceKey, string objectId, ObjectHistoryOutputRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>记录ID必须属于当前对象、已应用筛选和安全事件集合；不受列表当前页限制，仍每次重验完整读取。</summary>
    Task<ObjectHistoryItem?> GetAsync(string resourceKey, string objectId, string recordId, ObjectHistoryFilter filter,
        CancellationToken cancellationToken = default);
}

/// <summary>已接入正式按钮声明的owner使用完整读取查询；不再追加旧history数据动作。</summary>
public interface IReadableObjectHistoryQuery : IObjectHistoryQuery { }

/// <summary>履历owner输出的有限文件结果；不让业务owner依赖Audit私有服务或CRUD导出实现。</summary>
public sealed record ObjectHistoryFile(string FileName, string ContentType, byte[] Content);
/// <summary>同一安全查询集合的文件编码；资格/完整来源仍由当前owner入口和query共同重验。</summary>
public interface IObjectHistoryExport
{
    Task<ObjectHistoryFile?> ExportAsync(string resourceKey, string objectId, ObjectHistoryOutputRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>使用完整读取安全集合编码文件；旧独立owner仍保留原出口直到迁移。</summary>
public interface IReadableObjectHistoryExport : IObjectHistoryExport { }
