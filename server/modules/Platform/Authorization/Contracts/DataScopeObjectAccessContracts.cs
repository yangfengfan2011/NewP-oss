namespace ICP.Platform.Authorization.Contracts;

/// <summary>静态绑定事实在候选准备阶段核对，不能为了校验而构造请求作用域的实现。</summary>
public sealed record DataScopeResourceReadConstraintBinding(string Key, string ResourceKey, string ModuleId);

/// <summary>资源owner显式选用已登记的全局数据动作；不声明的旧资源保持原目录合同。</summary>
public sealed record DataScopeResourceActionBinding(string ResourceKey, string ModuleId, IReadOnlyList<string> ActionKeys);

/// <summary>资源拥有者的完整访问边界；与数据read相交，不参与底层读取计划递归。</summary>
public interface IDataScopeResourceReadConstraint
{
    string Key { get; }
    string ResourceKey { get; }
    string ModuleId { get; }

    /// <summary>按可信请求主体检查实际目标可达范围；不能输出私人位置事实。</summary>
    Task<bool> IsReadableAsync(string objectId, CancellationToken token = default);
}

/// <summary>已经按活动模型、owner、主键及静态约束绑定准备的唯一资源目录。</summary>
public interface IDataScopeResourceCatalog
{
    IReadOnlyList<DataScopeResource> Resources { get; }
    /// <summary>返回当前资源显式采用的动作；null表示旧资源未选用适用范围声明。</summary>
    IReadOnlyList<string>? ActionKeysFor(string resourceKey) => null;
}

/// <summary>完整对象访问交集数据read和已登记的资源约束；无声明保持原数据裁决。</summary>
public interface IDataScopeObjectAccess
{
    /// <summary>按当前可信主体检查既有规范对象；不存在、不规范或不可达均返回false。</summary>
    Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default);
}

/// <summary>真实owner在自身入口验证位置/功能后复用同一权限引擎查询，不切换请求主体。</summary>
public interface IDataScopeObjectPermissionQueries
{
    /// <summary>目标可浏览即可读取动作与主体类型，不公开任何完整对象直接配置。</summary>
    Task<DataScopePermissionCatalog?> CatalogAsync(string resourceKey, string objectId, CancellationToken token = default);
    /// <summary>账号选择仅公开最小用户候选，当前目标每次重新验证read；不开放其他配置主体候选。</summary>
    Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> UsersAsync(string resourceKey, string objectId, string? query,
        int page, IReadOnlyList<string> currentIds, CancellationToken token = default);
    /// <summary>返回指定真实账号的数据动作及受控依据；目标或账号不可查询时返回null，依赖故障传播。</summary>
    Task<DataScopeObjectInspection?> InspectAsync(string resourceKey, string objectId, string? accountId,
        CancellationToken token = default);
}

/// <summary>权限结果是数据动作裁决，不代替功能位置、业务状态或资源拥有者边界。</summary>
public sealed record DataScopeObjectInspection(string ResourceKey, string ObjectId, string AccountId, string AccountName,
    IReadOnlyList<DataScopeActionInspection> Actions,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DataScopeInspectionSubjectName>? SubjectNames = null);

/// <summary>新查询只投影本次依据实际出现主体的安全名称；不可见或缺失明确为unavailable，不扩候选目录。</summary>
public sealed record DataScopeInspectionSubjectName(string Type, string Id, string? Name, string Status);

/// <summary>数据动作定义的只读投影；实际完整执行资格仍由功能、业务和owner独立判定。</summary>
public sealed record DataScopePermissionAction(string Key, string Title, bool Enabled, bool RequiresRead);

/// <summary>当前对象可用的动作和直接项主体类型目录，不授予配置修改资格。</summary>
public sealed record DataScopePermissionCatalog(IReadOnlyList<DataScopePermissionAction> Actions, IReadOnlyList<DataScopeSubjectType> SubjectTypes);

/// <summary>动作结果和同次观察的命中来源。</summary>
public sealed record DataScopeActionInspection(string Key, string Title, bool Allowed, string Reason,
    IReadOnlyList<DataScopeInspectionSource> Sources);

/// <summary>保留精确来源及覆盖状态，不输出条件原文或私人位置。</summary>
public sealed record DataScopeInspectionSource(string Kind, string Id, string Name, string Opinion, string Status,
    IReadOnlyList<DataScopeInspectionSubject> Subjects);

/// <summary>命中主体的动作依据；业务档案内容仍由各自目录控制。</summary>
public sealed record DataScopeInspectionSubject(string Id, string Type, string? SubjectId, long Priority,
    IReadOnlyDictionary<string, string> Actions, DataScopeMatchOptions? MatchOptions,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ExpiresAt = null);
