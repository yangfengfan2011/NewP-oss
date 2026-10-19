using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataScopeObjectManagement
{
    /// <inheritdoc/>
    async Task<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>> IDataScopeBatchAuthorization.ReadCurrentAsync(
        IReadOnlyList<DataScopeAuthorizationTarget> targets, CancellationToken token)
    {
        using var observation = evaluation?.BeginOperation();
        return await transactions.ExecuteAsync<DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>>(async cancellation =>
        {
            var ordered = OrderTargets(targets);
            var status = await LockAndCheckTargetsAsync(ordered, cancellation);
            if (status != DataScopeObjectStatus.Succeeded) return new(AuthorizationStatus(status));
            var configurations = new List<DataScopeObjectConfiguration>();
            foreach (var target in ordered) configurations.Add(await grants.GetAsync(target.ResourceKey, target.ObjectId, cancellation));
            return new(DataScopeAuthorizationStatus.Succeeded, configurations);
        }, token);
    }

    /// <inheritdoc/>
    async Task<DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>> IDataScopeBatchAuthorization.ReplaceAsync(
        IReadOnlyList<DataScopeAuthorizationTarget> targets, DataScopeBatchConfiguration input, CancellationToken token)
    {
        var result = await WriteAsync(targets, (ordered, cancellation) => PrepareBatchAsync(ordered, input, cancellation), token);
        return new(AuthorizationStatus(result.Status), result.Value,
            result.ValidationError is null ? null : Error(result.ValidationError));
    }

    // U/R与技术ID属于批量输入语义；只在整组资格已成立后构造全部配置，尚不登记变化。
    private async Task<IReadOnlyList<DataScopeObjectDto>?> PrepareBatchAsync(
        IReadOnlyList<DataScopeAuthorizationTarget> ordered, DataScopeBatchConfiguration input, CancellationToken cancellation)
    {
        Dictionary<string, DataScopeObjectDto> prepared = new(StringComparer.Ordinal);
        if (input.Mode is not ("U" or "R") || input.Resources is null
            || input.Resources.Any(resource => resource is null || resource.Subjects is null)
            || input.Resources.Select(resource => resource.ResourceKey).Distinct(StringComparer.Ordinal).Count() != input.Resources.Count
            || !input.Resources.Select(resource => resource.ResourceKey).Order(StringComparer.Ordinal)
                .SequenceEqual(ordered.Select(target => target.ResourceKey).Distinct().Order(StringComparer.Ordinal))) return null;
        foreach (var resource in input.Resources)
        {
            if (resource.Subjects.Any(subject => subject is null)
                || input.Mode == "R" && resource.Subjects.Count == 0 && !input.ConfirmClear) return null;
            var subjects = resource.Subjects.Select(subject => new DataScopeDirectSubjectDto(ids.NewId("DataScopeObjectSubject"),
                subject.Type, subject.SubjectId, subject.Priority, subject.Actions, subject.MatchOptions, subject.ExpiresAt)).ToArray();
            prepared.Add(resource.ResourceKey, await grants.PrepareAsync(new(resource.ResourceKey,
                ordered.First(target => target.ResourceKey == resource.ResourceKey).ObjectId, subjects), cancellation));
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
        return configurations;
    }

    // 选择器身份只含规范主体及匹配范围；三态、优先级和期限均是整项替换内容。
    private static (string Type, string? Id, bool Descendants) Selector(DataScopeDirectSubjectDto subject) =>
        (subject.Type, subject.SubjectId, subject.MatchOptions?.IncludeDescendants == true);
}
