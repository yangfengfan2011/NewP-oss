using ICP.Platform.Authorization.Http;
namespace ICP.Platform.Authorization;
/// <summary>未装配功能目录时明确不可判定，避免空目录伪装成业务无权。</summary>
public sealed class IndeterminateFeatureDecisionSource : IFeatureDecisionSource
{
    /// <inheritdoc/>
    public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string behaviorKey, CancellationToken cancellationToken)
        => ValueTask.FromResult(PermissionDecision.Indeterminate);
}
