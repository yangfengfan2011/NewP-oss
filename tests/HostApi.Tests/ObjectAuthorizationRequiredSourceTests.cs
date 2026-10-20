using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectAuthorizationRequiredSourceTests
{
    [Theory]
    [InlineData("check")]
    [InlineData("configuration")]
    [InlineData("save")]
    [InlineData("subjects")]
    [InlineData("subject-sources")]
    [InlineData("batch-current")]
    [InlineData("batch-save")]
    [InlineData("catalog")]
    [InlineData("explanation")]
    [InlineData("users")]
    [InlineData("empty-catalog")]
    public async Task FormalOverloads_RejectMissingSourceBeforeAnyDependencyIsUsed(string operation)
    {
        var executor = new NeverAccess();
        var management = new DataScopeObjectManagement(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, executions: executor);
        var queries = new DataScopeObjectPermissionQueries(null!, null!, null!, null!, null!, null!, executions: executor);
        Task Invoke() => operation switch
        {
            "check" => management.CheckAsync("part.part", "target", null!),
            "configuration" => management.ReadAsync("part.part", "target", null!),
            "save" => management.ReplaceAsync("part.part", "target", new("part.part", "target", []), null!),
            "subjects" => management.SubjectsAsync("part.part", "target", "role", null, 1, [], (DataExecutionBindingReference)null!),
            "subject-sources" => management.SubjectsAsync("part.part", "target", "role", null, 1, [], (IReadOnlyList<DataExecutionBindingReference>)null!),
            "batch-current" => management.ReadCurrentAsync([new("part.part", "target")], null!),
            "batch-save" => management.ReplaceAsync([new("part.part", "target")], new("U", []), null!),
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
    [InlineData(typeof(DataScopeObjectManagement))]
    [InlineData(typeof(IDataScopeObjectPermissionQueries))]
    public void FormalRegistration_RequiresExecutionAccessBeforeChoosingACompatibilityConstructor(Type serviceType)
    {
        var services = new ServiceCollection();
        new ICP.Platform.Modules.Authorization.AuthorizationModule().ConfigureServices(services);
        services.RemoveAll<IDataExecutionAccess>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var failure = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService(serviceType));
        Assert.Contains(typeof(IDataExecutionAccess).FullName!, failure.Message, StringComparison.Ordinal);
    }

    private sealed class NeverAccess : IDataExecutionAccess
    {
        public void Validate(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey) => throw new InvalidOperationException("unexpected dependency");
        public Task<DataExecutionDecision> EvaluateObjectAsync(DataExecutionBindingReference binding, string resourceKey, string role, string objectId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("unexpected dependency");
        public Task<DataExecutionDecision> EvaluateCandidateAsync(DataExecutionBindingReference binding, string resourceKey, string role,
            IReadOnlyDictionary<string, object?> candidate, CancellationToken cancellationToken = default) => throw new InvalidOperationException("unexpected dependency");
    }
}
