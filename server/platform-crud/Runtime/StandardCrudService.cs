using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudService : IStandardCrudService
{
    private readonly IStandardCrudRepository _repository;
    private readonly IReadOnlyDictionary<string, IStandardCrudQueryCustomizer> _queryCustomizers;
    private readonly IReadOnlyDictionary<string, IStandardCrudDisplayValueResolver> _displayResolvers;
    private readonly IReadOnlyDictionary<(string ResourceKey, string Key), IStandardCrudSummaryProvider> _summaryProviders;
    private readonly IReadOnlyDictionary<string, IStandardCrudValidator> _validators;
    private readonly IReadOnlyDictionary<(string ResourceKey, string ActionKey), IStandardCrudActionHandler> _actionHandlers;
    private readonly IReadOnlyDictionary<string, IStandardCrudDeleteGuard> _deleteGuards;

    public StandardCrudService(
        IStandardCrudRepository repository,
        IEnumerable<IStandardCrudQueryCustomizer> queryCustomizers,
        IEnumerable<IStandardCrudDisplayValueResolver> displayResolvers,
        IEnumerable<IStandardCrudSummaryProvider> summaryProviders,
        IEnumerable<IStandardCrudValidator> validators,
        IEnumerable<IStandardCrudActionHandler> actionHandlers,
        IEnumerable<IStandardCrudDeleteGuard> deleteGuards)
    {
        _repository = repository;
        _queryCustomizers = queryCustomizers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _displayResolvers = displayResolvers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _summaryProviders = summaryProviders.ToDictionary(item => (item.ResourceKey, item.Key));
        _validators = validators.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _actionHandlers = actionHandlers.ToDictionary(item => (item.ResourceKey, item.ActionKey));
        _deleteGuards = deleteGuards.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
    }

    /// <summary>
    /// 列表读取先校验 descriptor，再将模块查询扩展收敛为仓储条件，最后补齐显示值。
    /// </summary>
    public async Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var conditions = BuildConditions(descriptor, query);
        var result = await _repository.ListAsync(descriptor, query, conditions, cancellationToken);
        var items = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var row in result.Items)
        {
            items.Add(await ApplyDisplayValuesAsync(descriptor, row, cancellationToken));
        }

        return result with { Items = items };
    }

    /// <summary>
    /// 详情读取只对声明的摘要 key 调用 provider；provider 缺失或失败都转为稳定状态块。
    /// </summary>
    public async Task<StandardCrudDetail?> GetDetailAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var row = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var fields = await ApplyDisplayValuesAsync(descriptor, row, cancellationToken);
        var summaries = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
        foreach (var key in descriptor.SummaryBlockKeys)
        {
            summaries[key] = await BuildSummaryAsync(descriptor, fields, key, cancellationToken);
        }

        return new StandardCrudDetail { Fields = fields, Summaries = summaries };
    }

    /// <summary>
    /// options 当前阶段保持轻量透传，但仍先做 descriptor 静态校验。
    /// </summary>
    public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        return _repository.OptionsAsync(descriptor, query, cancellationToken);
    }

    public Task<StandardCrudMutationResult> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.CreateNotEnabled",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用创建主干。",
            ReasonKey = "standardCrud.createNotEnabled"
        }));
    }

    public Task<StandardCrudMutationResult> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.UpdateNotEnabled",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用更新主干。",
            ReasonKey = "standardCrud.updateNotEnabled"
        }));
    }

    public Task<StandardCrudMutationResult> DisableAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.DisableNotEnabled",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用停用主干。",
            ReasonKey = "standardCrud.disableNotEnabled"
        }));
    }

    public Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.DeleteNotEnabled",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用删除主干。",
            ReasonKey = "standardCrud.deleteNotEnabled"
        }));
    }

    // 查询扩展点只产出标准条件，仓储仍负责具体执行，避免模块逻辑直接进入持久化实现。
    private IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        return _queryCustomizers.TryGetValue(descriptor.ResourceKey, out var customizer)
            ? customizer.BuildConditions(descriptor, query)
            : [];
    }

    // 显示值补齐以 row copy 方式合并，确保扩展点不能修改仓储返回的原始字段集合。
    private async Task<IReadOnlyDictionary<string, object?>> ApplyDisplayValuesAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(row, StringComparer.Ordinal);
        if (_displayResolvers.TryGetValue(descriptor.ResourceKey, out var resolver))
        {
            foreach (var pair in await resolver.ResolveAsync(descriptor, row, cancellationToken))
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }

    // 摘要 provider 是跨模块依赖入口，失败时返回稳定状态，避免详情主数据被附属摘要拖垮。
    private async Task<StandardCrudSummaryBlock> BuildSummaryAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        string key,
        CancellationToken cancellationToken)
    {
        if (!_summaryProviders.TryGetValue((descriptor.ResourceKey, key), out var provider))
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        try
        {
            return await provider.BuildAsync(descriptor, row, cancellationToken);
        }
        catch (Exception)
        {
            return new StandardCrudSummaryBlock { Status = "dependencyUnavailable" };
        }
    }
}
