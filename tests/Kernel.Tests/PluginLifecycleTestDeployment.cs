using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;

namespace ICP.Platform.Kernel.Tests;

// 运维单元夹具明确只读取 raw 生命周期；激活和真实 SQL 的确认由各自正式 adapter 验证。
internal sealed class PluginLifecycleTestDeployment : IPluginActivationDeployment
{
    public CompiledPersistencePlan Compile(CanonicalModulePlan modulePlan) => throw new NotSupportedException();
    public void PrepareCandidate(CanonicalModulePlan modulePlan, CompiledPersistencePlan persistencePlan) => throw new NotSupportedException();
    public void CompleteCandidate(CompiledPersistencePlan persistencePlan) => throw new NotSupportedException();
    public bool IsSnapshotCommitted(PluginActiveSnapshot snapshot) => true;
}
