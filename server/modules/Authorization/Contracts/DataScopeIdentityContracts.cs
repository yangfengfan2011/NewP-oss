using System.Text.Json.Serialization;

namespace ICP.Platform.Authorization.Contracts;

/// <summary>主体匹配选项属于主体身份，不能作为对象属性条件。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DataScopeMatchOptions(bool IncludeDescendants = false);
/// <summary>模块提供的主体类型及其可配置匹配能力。</summary>
public sealed record DataScopeSubjectType(string Key, string Title, bool RequiresId, bool SupportsDescendants = false);
/// <summary>当前身份值的规范类型和事实 owner。</summary>
public sealed record DataScopeContextValue(string Key, string Title, string ValueType, string Source);
/// <summary>资源 owner 声明直接字段与规范身份域的绑定，不从 CLR string 推测引用语义。</summary>
public sealed record DataScopeContextBinding(string ModelKey, string Field, string ContextKey);
/// <summary>管理目录只公开身份、名称及必要层级；不携带业务档案。</summary>
public sealed record DataScopeSubjectName(string Id, string Name, string? ParentId = null);
/// <summary>分页目录及已有授权项的当前名称来自同一 owner。</summary>
public sealed record DataScopeSubjectPage(IReadOnlyList<DataScopeSubjectName> Items, bool HasMore,
    IReadOnlyList<DataScopeSubjectName> CurrentValues);

/// <summary>每次裁决解析一次可信当前事实；关系缺失返回不匹配，事实源故障抛出。</summary>
public interface IDataScopeIdentityFacts
{
    /// <summary>只匹配该 provider 声明的类型及已校验选项。</summary>
    bool Matches(string type, string? id, DataScopeMatchOptions options);
    /// <summary>返回规范身份值；合法缺失为 null，不表示故障。</summary>
    string? ContextValue(string key);
}

/// <summary>模块通过正式 DI 注册主体和上下文扩展，授权核心不识别模块业务字段。</summary>
public interface IDataScopeIdentityProvider
{
    IReadOnlyList<DataScopeSubjectType> SubjectTypes { get; }
    IReadOnlyList<DataScopeContextValue> ContextValues { get; }
    IReadOnlyList<DataScopeContextBinding> ContextBindings => [];
    /// <summary>accountId 必须由服务端认证主体传入；返回当前 owner 已发布事实。</summary>
    Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token);
    /// <summary>读取管理用最小目录，搜索与分页在数据源执行，currentIds 仅用于已有值回显。</summary>
    Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token);
}
