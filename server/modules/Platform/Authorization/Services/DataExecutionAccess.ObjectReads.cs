using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataExecutionAccess
{
    // 复用原现时对象读取/动作裁决，不用候选接口替代ACL或缓存前阶段允许结果。
    private async Task<DataExecutionDecision> EvaluateObjectRequirementAsync(DataExecutionRequirement requirement, string resourceKey,
        string objectId, CancellationToken cancellationToken)
    {
        foreach (var branch in requirement.AnyOf)
        {
            var allowed = true;
            foreach (var action in branch)
            {
                if (action == "read")
                {
                    if (!await reads.IsReadableAsync(resourceKey, objectId, cancellationToken)) return DataExecutionDecision.NotFound;
                }
                else if (!await decisions.IsAllowedAsync(resourceKey, objectId, action, cancellationToken)) { allowed = false; break; }
            }
            if (allowed) return DataExecutionDecision.Allowed;
        }
        return DataExecutionDecision.Denied;
    }

}
