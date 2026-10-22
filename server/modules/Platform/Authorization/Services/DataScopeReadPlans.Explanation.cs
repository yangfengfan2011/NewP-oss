using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataScopeReadPlans
{
    /// <summary>管理入口校验用户后，使用正式裁决解释该用户对既有对象的动作权限。</summary>
    public async Task<IReadOnlyList<DataScopeActionExplanationDto>> ExplainAsync(string resourceKey,
        string objectId, string accountId, CancellationToken token)
    {
        using var operation = observation.BeginOperation();
        using var configuration = rules.BeginOperation();
        var results = new List<DataScopeActionExplanationDto>();
        // 新建必须用新候选，不能将既有记录的字段伪装为将来创建时的事实。
        foreach (var action in catalog.ActionsFor(resourceKey).Where(action => action.Key != "create"))
        {
            var trace = new DataScopeDecisionTrace(objectId);
            var allowed = await DecideActionAsync(resourceKey, objectId, action.Key, null, token, accountId, trace);
            results.Add(trace.Result(action.Key, action.Title, allowed));
        }
        return results;
    }
}
