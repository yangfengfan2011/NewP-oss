using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>校验扩展目录并按需要解析一次当前事实，不跨请求保存身份快照。</summary>
public sealed class DataScopeIdentities
{
    private readonly IReadOnlyList<IDataScopeIdentityProvider> providers;
    public IReadOnlyList<DataScopeSubjectType> SubjectTypes { get; }
    public IReadOnlyList<DataScopeContextValue> ContextValues { get; }
    public IReadOnlyList<DataScopeContextBinding> ContextBindings { get; }

    // 重复注册必须失败，不能依赖 DI 顺序覆盖扩展。
    public DataScopeIdentities(IEnumerable<IDataScopeIdentityProvider> providers)
    {
        this.providers = providers.ToArray();
        SubjectTypes = this.providers.SelectMany(provider => provider.SubjectTypes).ToArray();
        ContextValues = this.providers.SelectMany(provider => provider.ContextValues).ToArray();
        ContextBindings = this.providers.SelectMany(provider => provider.ContextBindings).ToArray();
        if (SubjectTypes.Select(type => type.Key).Distinct(StringComparer.Ordinal).Count() != SubjectTypes.Count
            || ContextValues.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count() != ContextValues.Count)
            throw new InvalidOperationException("DATA_SCOPE_IDENTITY_REGISTRATION_CONFLICT");
    }

    // 只将有效的主体引用和该类型支持的选项交给 owner。
    public void Validate(string type, string? id, DataScopeMatchOptions? options)
    {
        var definition = SubjectTypes.SingleOrDefault(item => item.Key == type)
            ?? throw new RolePermissionValidationException("主体类型未登记。");
        if (definition.RequiresId ? string.IsNullOrWhiteSpace(id) || id.Length > 128 : id is not null)
            throw new RolePermissionValidationException("主体身份无效。");
        if (options?.IncludeDescendants == true && !definition.SupportsDescendants)
            throw new RolePermissionValidationException("该主体类型不支持包含下级。");
    }

    // 管理功能流水线负责调用资格；目录只返回经过 owner 投影的身份字段。
    public Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token)
    {
        if (page < 1) throw new RolePermissionValidationException("主体目录页码无效。");
        return ProviderForSubject(type).SearchAsync(type, query, page, currentIds, token);
    }

    // 每个被实际引用的 provider 只解析一次；未使用的事实源不引入额外依赖。
    public async Task<DataScopeResolvedIdentities> ResolveAsync(string accountId, IEnumerable<string> types, IEnumerable<string> keys, CancellationToken token)
    {
        var selected = types.Select(ProviderForSubject).Concat(keys.Select(ProviderForContext)).Distinct().ToArray();
        var facts = new Dictionary<IDataScopeIdentityProvider, IDataScopeIdentityFacts>();
        foreach (var provider in selected) facts[provider] = await provider.ResolveAsync(accountId, token);
        return new(this, facts);
    }

    // 目录缺失是配置故障，不能当成主体不匹配。
    internal IDataScopeIdentityProvider ProviderForSubject(string type) => providers.SingleOrDefault(provider => provider.SubjectTypes.Any(item => item.Key == type))
        ?? throw new RolePermissionValidationException("主体类型未登记。");
    // 上下文必须引用已登记的有类型值。
    internal IDataScopeIdentityProvider ProviderForContext(string key) => providers.SingleOrDefault(provider => provider.ContextValues.Any(item => item.Key == key))
        ?? throw new RolePermissionValidationException("当前值未登记。");
}

/// <summary>一次裁决中的解析结果，多个规则和 SQL 中的业务行共享它。</summary>
public sealed class DataScopeResolvedIdentities(DataScopeIdentities catalog, IReadOnlyDictionary<IDataScopeIdentityProvider, IDataScopeIdentityFacts> facts)
{
    // 主体匹配不再访问数据库或当前请求外的缓存。
    public bool Matches(string type, string? id, DataScopeMatchOptions? options) => facts[catalog.ProviderForSubject(type)].Matches(type, id, options ?? new());
    // 对象授权的 SQL 关系筛选只使用事实 owner 列出的当前身份，不加载对象授权全集。
    public IReadOnlyList<DataScopeSubjectMembership> Memberships(string type) => facts[catalog.ProviderForSubject(type)].Memberships(type);
    // null 仅表示 owner 的合法关系缺失。
    public string? ContextValue(string key) => facts[catalog.ProviderForContext(key)].ContextValue(key);
}
