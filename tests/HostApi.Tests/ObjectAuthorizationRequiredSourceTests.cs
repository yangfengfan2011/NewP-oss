using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectAuthorizationRequiredSourceTests
{
    [Theory]
    [InlineData("catalog")]
    [InlineData("explanation")]
    [InlineData("users")]
    [InlineData("empty-catalog")]
    public async Task FormalOverloads_RejectMissingSourceBeforeAnyDependencyIsUsed(string operation)
    {
        var executor = new NeverAccess();
        var queries = new DataScopeObjectPermissionQueries(executor, null!, null!, null!, null!, null!);
        Task Invoke() => operation switch
        {
            "catalog" => queries.CatalogAsync("part.part", "target", null!),
            "explanation" => queries.InspectAsync("part.part", "target", null, null!),
            "users" => queries.UsersAsync("part.part", "target", null, 1, [], null!),
            "empty-catalog" => queries.CatalogAsync("part.part", "target", []),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var rejected = await Assert.ThrowsAsync<ActionDataAccessException>(Invoke);
        Assert.Equal(400, rejected.Status);
    }

    [Theory]
    [InlineData(typeof(DataScopeObjectManagement), typeof(IObjectActionAccess))]
    [InlineData(typeof(IDataScopeObjectPermissionQueries), typeof(IDataExecutionAccess))]
    public void FormalRegistration_RequiresTheActualTypedDependency(Type serviceType, Type dependency)
    {
        var services = new ServiceCollection();
        new ICP.Platform.Modules.Authorization.AuthorizationModule().ConfigureServices(services);
        services.RemoveAll(dependency);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var failure = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService(serviceType));
        Assert.Contains(dependency.FullName!, failure.Message, StringComparison.Ordinal);
    }

    private sealed class NeverAccess : IDataExecutionAccess
    {
        // 未消费的能力明确抛出，字段/拒绝测试不能继承生产接口的过渡实现。
        public Task<DataScopeReadPlan> GetQueryReadPlanAsync(DataExecutionQueryContext context, string resourceKey,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DataScopeReadPlan> GetObjectReadPlanAsync(IReadOnlyList<DataExecutionBindingReference> bindings,
            string resourceKey, string role, string objectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetInternalReadableIdsAsync(DataExecutionInternalReadBinding binding,
            string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DataScopeReadPlan> GetInternalReadPlanAsync(DataExecutionInternalReadBinding binding,
            string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DataScopeReadPlan> GetInternalQueryReadPlanAsync(DataExecutionInternalReadBinding binding,
            string resourceKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DataScopeReadPlan> GetApiQueryReadPlanAsync(DataExecutionApiQueryContext context,
            string resourceKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DataScopeReadPlan> GetApiObjectReadPlanAsync(DataExecutionApiObjectContext context, string resourceKey, string objectId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DataExecutionTargetDecision> EvaluateApiObjectAsync(DataExecutionApiObjectExecutionContext context, string role,
            DataExecutionTargetInput target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<DataExecutionTargetDecision> EvaluateObjectsAsync(DataExecutionBindingReference binding, string role,
            IReadOnlyList<DataExecutionTargetInput> targets, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DataExecutionDecision> EvaluateObjectFromBindingsAsync(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey,
            string role, string objectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DataExecutionTargetDecision> EvaluateInternalObjectAsync(DataExecutionInternalBinding binding, string role,
            DataExecutionTargetInput target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Validate(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey) => throw new InvalidOperationException("unexpected dependency");
        public Task<DataExecutionDecision> EvaluateObjectAsync(DataExecutionBindingReference binding, string resourceKey, string role, string objectId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("unexpected dependency");

    }
}
