using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationConditionalDeleteObjectOperationTests
{
    [Theory]
    [InlineData("company")]
    [InlineData("position")]
    public async Task DeleteAsync_WhenAdapterIsCalledDirectly_EntersDeclaredObjectOperation(string resource)
    {
        await using var context = CreateContext();
        var operations = new RecordingObjectOperations();
        var conditionalDelete = new OrganizationConditionalDeleteExecutor(
            context,
            () => operations,
            new InactiveExecutionContext());
        var (descriptor, executor) = resource switch
        {
            "company" => (CompanyCrudDescriptorFactory.Create(),
                (IStandardCrudConditionalDeleteExecutor)new CompanyCrudDeleteExecutor(context, conditionalDelete)),
            "position" => (PositionCrudDescriptorFactory.Create(),
                new PositionCrudDeleteExecutor(context, conditionalDelete)),
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null)
        };

        var result = await executor.DeleteAsync(descriptor, "Requested_Alias");

        Assert.True(result.Succeeded);
        var call = Assert.Single(operations.Calls);
        Assert.Equal(descriptor.ResourceKey, call.ObjectKey);
        Assert.Equal(ObjectOperationAction.Delete, call.Action);
        Assert.Equal("Requested_Alias", call.TargetId);
        Assert.Empty(call.Values);
    }

    private static EfPlatformDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        var definitions = PlatformPersistenceModelCatalog.AdaptPlatformCore(NonSqlPlatformBaselineModelSource.GetDefinitions())
            .Concat(IdentityModelDefinitions.All)
            .Concat(AuthenticationModelDefinitions.All)
            .ToArray();
        return new EfPlatformDbContext(options, definitions);
    }

    private sealed class RecordingObjectOperations : IObjectOperationExecutor
    {
        public List<Call> Calls { get; } = [];

        public bool CanExecute(string objectKey, ObjectOperationAction action) =>
            objectKey is "organization.company" or "organization.position"
            && action == ObjectOperationAction.Delete;

        public Task<StandardCrudMutationResult> ExecuteAsync(string objectKey, ObjectOperationAction action, string? targetId,
            IReadOnlyDictionary<string, object?> values, ObjectOperationWriteSource? source, CancellationToken token = default)
        {
            Assert.IsType<ObjectOperationWriteSource.Declared>(source);
            return ExecuteAsync(objectKey, action, targetId, values, token);
        }
        public Task<StandardCrudMutationResult> ExecuteAsync(
            string objectKey,
            ObjectOperationAction action,
            string? targetId,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call(objectKey, action, targetId, values));
            return Task.FromResult(StandardCrudMutationResult.Success());
        }
    }

    private sealed class InactiveExecutionContext : IObjectOperationExecutionContext
    {
        public bool IsExecuting(string objectKey, ObjectOperationAction action, string? targetId) => false;
    }

    private sealed record Call(
        string ObjectKey,
        ObjectOperationAction Action,
        string? TargetId,
        IReadOnlyDictionary<string, object?> Values);
}
