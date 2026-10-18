using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>批量直接项的唯一编排：全目标锁与资格成立后才登记变化，共用单对象持久化核心。</summary>
public sealed class DataScopeBatchAuthorization(DataScopeObjectGrants grants, IDataScopeObjectAuthorization authorization,
    IPlatformTransactionExecutor transactions, IEntityAuditValueProvider audit, IPlatformObjectIdGenerator ids,
    IDataScopeEvaluationScope evaluation) : IDataScopeBatchAuthorization
{
    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>> ReadCurrentAsync(
        IReadOnlyList<DataScopeAuthorizationTarget> targets, CancellationToken token = default)
    {
        using var observation = evaluation.BeginOperation();
        return await transactions.ExecuteAsync<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>>(async cancellation =>
        {
            var ordered = targets.Distinct().OrderBy(target => target.ResourceKey, StringComparer.Ordinal)
                .ThenBy(target => target.ObjectId, StringComparer.Ordinal).ToArray();
            if (ordered.Length == 0 || ordered.Any(target => string.IsNullOrWhiteSpace(target.ResourceKey)
                || string.IsNullOrWhiteSpace(target.ObjectId) || target.ObjectId.Length > 128))
                return new(DataScopeAuthorizationStatus.InvalidInput);
            foreach (var target in ordered) await grants.LockAsync(target.ResourceKey, target.ObjectId, cancellation);
            foreach (var target in ordered)
            {
                var status = await authorization.CheckAsync(target.ResourceKey, target.ObjectId, cancellation);
                if (status != DataScopeAuthorizationStatus.Succeeded) return new(status);
            }
            var configurations = new List<DataScopeObjectConfiguration>();
            foreach (var target in ordered) configurations.Add(await grants.GetAsync(target.ResourceKey, target.ObjectId, cancellation));
            return new(DataScopeAuthorizationStatus.Succeeded, configurations);
        }, token);
    }

    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>> ReplaceAsync(
        IReadOnlyList<DataScopeAuthorizationTarget> targets, DataScopeBatchConfiguration input, CancellationToken token = default)
    {
        using var observation = evaluation.BeginOperation();
        using var operation = audit.BeginOperation();
        return await transactions.ExecuteAsync<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>>(async cancellation =>
        {
            var ordered = targets.Distinct().OrderBy(target => target.ResourceKey, StringComparer.Ordinal)
                .ThenBy(target => target.ObjectId, StringComparer.Ordinal).ToArray();
            if (ordered.Length == 0 || ordered.Any(target => string.IsNullOrWhiteSpace(target.ResourceKey)
                || string.IsNullOrWhiteSpace(target.ObjectId) || target.ObjectId.Length > 128)) return Invalid();
            foreach (var target in ordered) await grants.LockAsync(target.ResourceKey, target.ObjectId, cancellation);
            foreach (var target in ordered)
            {
                var status = await authorization.CheckAsync(target.ResourceKey, target.ObjectId, cancellation);
                if (status != DataScopeAuthorizationStatus.Succeeded) return new(status);
            }
            Dictionary<string, DataScopeObjectDto> prepared = new(StringComparer.Ordinal);
            try
            {
                if (input.Mode is not ("U" or "R") || input.Resources is null
                    || input.Resources.Any(resource => resource is null || resource.Subjects is null)
                    || input.Resources.Select(resource => resource.ResourceKey).Distinct(StringComparer.Ordinal).Count() != input.Resources.Count
                    || !input.Resources.Select(resource => resource.ResourceKey).Order(StringComparer.Ordinal)
                        .SequenceEqual(ordered.Select(target => target.ResourceKey).Distinct().Order(StringComparer.Ordinal))) return Invalid();
                foreach (var resource in input.Resources)
                {
                    if (resource.Subjects.Any(subject => subject is null)
                        || input.Mode == "R" && resource.Subjects.Count == 0 && !input.ConfirmClear) return Invalid();
                    var subjects = resource.Subjects.Select(subject => new DataScopeDirectSubjectDto(ids.NewId("DataScopeObjectSubject"),
                        subject.Type, subject.SubjectId, subject.Priority, subject.Actions, subject.MatchOptions, subject.ExpiresAt)).ToArray();
                    prepared.Add(resource.ResourceKey, await grants.PrepareAsync(new(resource.ResourceKey,
                        ordered.First(target => target.ResourceKey == resource.ResourceKey).ObjectId, subjects), cancellation));
                }
            }
            catch (RolePermissionValidationException error)
            {
                return new(DataScopeAuthorizationStatus.InvalidInput, Error: new(error.Message, error.Code,
                    "AuthorizationErrors." + error.Code, error.Args, error.Field));
            }
            var configurations = new List<DataScopeObjectDto>();
            foreach (var target in ordered)
            {
                var existing = await grants.GetAsync(target.ResourceKey, target.ObjectId, cancellation);
                var entries = existing.Subjects.ToDictionary(Selector);
                var selected = prepared[target.ResourceKey].Subjects.Select(subject => subject with {
                    Id = entries.TryGetValue(Selector(subject), out var current) ? current.Id : ids.NewId("DataScopeObjectSubject") }).ToArray();
                var selectors = selected.Select(Selector).ToHashSet();
                configurations.Add(new(target.ResourceKey, target.ObjectId, input.Mode == "R" ? selected
                    : existing.Subjects.Where(subject => !selectors.Contains(Selector(subject))).Concat(selected).ToArray()));
            }
            var changed = 0;
            foreach (var configuration in configurations)
                if (await grants.ApplyAsync(configuration, cancellation)) changed++;
            return new(DataScopeAuthorizationStatus.Succeeded, new(true, ordered.Length, changed));
        }, token);
    }

    // 选择器身份只含规范主体及匹配范围；三态、优先级和期限均是整项替换内容。
    private static (string Type, string? Id, bool Descendants) Selector(DataScopeDirectSubjectDto subject) =>
        (subject.Type, subject.SubjectId, subject.MatchOptions?.IncludeDescendants == true);

    // 无法形成完整批次时拒绝且不泄漏哪个当前对象具备配置。
    private static DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt> Invalid() =>
        new(DataScopeAuthorizationStatus.InvalidInput);
}
