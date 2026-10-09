using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Modules.Example.Crud;

/// <summary>
/// Example 自有的 reviewer user display resolver；只依赖 Identity 的批量目录契约，不接触其持久化实现。
/// </summary>
public sealed class ExampleRecordDisplayValueResolver(IIdentityAccountDirectory accountDirectory) : IStandardCrudDisplayValueResolver
{
    public string ResourceKey => "example.record";

    /// <summary>
    /// 仅为当前请求的 canonical user companion 批量解析显示名，保留 raw EID 并避免逐行目录查询。
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default)
    {
        var requested = requestedDisplayFields
            .Where(static field => string.Equals(field.Field, "ReviewerUserEID_DisplayValue", StringComparison.Ordinal))
            .ToArray();
        if (requested.Length == 0)
        {
            return rows.Select(static _ => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)).ToArray();
        }

        var resolution = await TryResolveAccountsAsync(rows, cancellationToken);
        return rows.Select(row =>
        {
            var displayValues = new Dictionary<string, object?>(StringComparer.Ordinal);
            displayValues["ReviewerUserEID_DisplayValue"] = ReadDisplayName(row, resolution);
            return (IReadOnlyDictionary<string, object?>)displayValues;
        }).ToArray();
    }

    /// <summary>
    /// Identity 目录是 display companion 的非关键依赖；任何取消都直接传播，其余目录失败降级为未解析。
    /// </summary>
    private async Task<AccountResolution> TryResolveAccountsAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        CancellationToken cancellationToken)
    {
        try
        {
            return new AccountResolution(
                await accountDirectory.FindManyAsync(CollectAccountEids(rows), cancellationToken),
                IsDirectoryAvailable: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new AccountResolution(
                new Dictionary<string, IdentityAccountSnapshot>(StringComparer.Ordinal),
                IsDirectoryAvailable: false);
        }
    }

    /// <summary>
    /// 从当前批次收集非空 reviewer raw EID，并按首次出现顺序去重后交给目录批量解析。
    /// </summary>
    private static IReadOnlyCollection<string> CollectAccountEids(IEnumerable<IReadOnlyDictionary<string, object?>> rows) =>
        rows.Select(ReadReviewerUserEid)
            .Where(static accountEid => !string.IsNullOrWhiteSpace(accountEid))
            .Select(static accountEid => accountEid!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// raw 为空时 companion 为空；目录可用但未命中时 companion 回退 raw，目录不可用时 companion 为 null 且 raw 由 merge 保留。
    /// </summary>
    private static object? ReadDisplayName(
        IReadOnlyDictionary<string, object?> row,
        AccountResolution resolution) =>
        ReadReviewerUserEid(row) is not { } accountEid
            ? null
            : resolution.Accounts.TryGetValue(accountEid, out var account)
                ? account.ENAME
                : resolution.IsDirectoryAvailable
                    ? accountEid
                    : null;

    /// <summary>
    /// 读取当前行的 reviewer raw EID，并维持标准 CRUD 对空值的规范化语义。
    /// </summary>
    private static string? ReadReviewerUserEid(IReadOnlyDictionary<string, object?> row) =>
        row.TryGetValue("ReviewerUserEID", out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()
            : null;

    /// <summary>
    /// 封装单批目录解析结果，并显式区分“目录不可用”和“目录可用但账号未命中”。
    /// </summary>
    private sealed record AccountResolution(
        IReadOnlyDictionary<string, IdentityAccountSnapshot> Accounts,
        bool IsDirectoryAvailable);
}
