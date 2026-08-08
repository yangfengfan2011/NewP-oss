namespace ICP.Platform.HostApi.Readiness;

/// <summary>
/// Host readiness 的默认有序聚合器。
/// </summary>
public sealed class ReadinessEvaluator : IReadinessEvaluator
{
    private readonly IReadOnlyList<IReadinessContributor> _contributors;

    public ReadinessEvaluator(IEnumerable<IReadinessContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        _contributors = contributors
            .OrderBy(contributor => contributor.Order)
            .ThenBy(contributor => contributor.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ReadinessEvaluation> EvaluateAsync(CancellationToken cancellationToken)
    {
        var stages = new List<EvaluatedStage>(_contributors.Count);

        foreach (var contributor in _contributors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadinessContribution contribution;
            try
            {
                contribution = await contributor.CheckAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Host 停止或请求断开必须立即向上传播，readiness 不能把退出误报成阶段失败。
                throw;
            }
            catch (Exception)
            {
                // contributor 的异常内容可能包含路径或连接信息，只返回稳定、脱敏的宿主诊断。
                contribution = new ReadinessContribution(
                    ReadinessState.Failed,
                    ReadinessFailureCodes.ContributorException,
                    "Readiness contributor check failed.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            stages.Add(new EvaluatedStage(
                contributor.IsRequired,
                contribution.State,
                new ReadinessStageDto(
                    contributor.Name,
                    ToStatus(contribution.State),
                    contributor.IsRequired,
                    NormalizeFailureCode(contribution),
                    contribution.Diagnostic)));
        }

        var requiredStages = stages.Where(stage => stage.Required).ToArray();
        if (requiredStages.Length == 0)
        {
            return NotReady(
                ReadinessState.Indeterminate,
                ReadinessFailureCodes.RequiredContributorsMissing,
                stages);
        }

        var blockingStage = FindBlockingStage(requiredStages);
        if (blockingStage is not null)
        {
            return NotReady(
                blockingStage.State,
                blockingStage.Dto.FailureCode!,
                stages);
        }

        return new ReadinessEvaluation(
            IsReady: true,
            new ReadinessResponseDto(
                Status: "ready",
                FailureCode: null,
                Stages: stages.Select(stage => stage.Dto).ToArray()));
    }

    private static EvaluatedStage? FindBlockingStage(IReadOnlyCollection<EvaluatedStage> requiredStages) =>
        requiredStages.FirstOrDefault(stage => stage.State is ReadinessState.Failed)
        ?? requiredStages.FirstOrDefault(stage => stage.State is ReadinessState.Starting)
        ?? requiredStages.FirstOrDefault(stage => stage.State is ReadinessState.Indeterminate);

    private static ReadinessEvaluation NotReady(
        ReadinessState state,
        string failureCode,
        IReadOnlyCollection<EvaluatedStage> stages) =>
        new(
            IsReady: false,
            new ReadinessResponseDto(
                Status: ToStatus(state),
                FailureCode: failureCode,
                Stages: stages.Select(stage => stage.Dto).ToArray()));

    private static string? NormalizeFailureCode(ReadinessContribution contribution)
    {
        if (contribution.State is ReadinessState.Ready)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(contribution.FailureCode))
        {
            return contribution.FailureCode;
        }

        return contribution.State switch
        {
            ReadinessState.Starting => ReadinessFailureCodes.ContributorStarting,
            ReadinessState.Failed => ReadinessFailureCodes.ContributorFailed,
            ReadinessState.Indeterminate => ReadinessFailureCodes.ContributorIndeterminate,
            _ => ReadinessFailureCodes.ContributorIndeterminate
        };
    }

    private static string ToStatus(ReadinessState state) =>
        state switch
        {
            ReadinessState.Starting => "starting",
            ReadinessState.Ready => "ready",
            ReadinessState.Failed => "failed",
            ReadinessState.Indeterminate => "indeterminate",
            _ => "indeterminate"
        };

    private sealed record EvaluatedStage(
        bool Required,
        ReadinessState State,
        ReadinessStageDto Dto);
}
