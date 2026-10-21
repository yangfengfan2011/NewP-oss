using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Persistence.Abstractions;
using Xunit;

namespace ICP.Platform.Crud.Tests;

/// <summary>启停仍使用更新核心，但入口身份、受保护旧值和固定状态字段不能变成通用更新来源。</summary>
public sealed class StandardCrudStateButtonTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task StateEntry_PreservesItsOwnBindingAndProtectedUpdate(bool enabled, bool denied)
    {
        var transaction = new Transaction();
        var repository = new Repository(transaction);
        var buttons = new Buttons(transaction, enabled ? "enable" : "disable", denied);
        var access = new ICP.Platform.Testing.StandardCrudTestAccess();
        var writer = new StandardCrudDefaultWriter(access, repository, [], [], transactionExecutor: transaction, buttons: buttons);
        var service = new StandardCrudService(access, null, repository, null!, writer, [], new DisplayValueProjection([]), [], access, buttons: buttons);
        var descriptor = new StandardCrudDescriptor
        {
            ModuleId = "Test", ResourceKey = "test.item", ModelName = "Item", Route = "api/items", IdField = "EID", DisplayField = "ENAME", EnabledField = "IsEnabled",
            FieldSet = new(["EID", "ENAME", "IsEnabled"], ["EID", "ENAME", "IsEnabled"], ["ENAME"], ["ENAME", "IsEnabled"])
        };
        if (denied)
            await Assert.ThrowsAsync<ActionDataAccessException>(() => enabled ? service.EnableAsync(descriptor, "Item_official") : service.DisableAsync(descriptor, "Item_official"));
        else
        {
            var result = await (enabled ? service.EnableAsync(descriptor, "Item_official") : service.DisableAsync(descriptor, "Item_official"));
            Assert.True(result.Succeeded);
            Assert.Equal(enabled, result.Detail!.Fields["IsEnabled"]);
            Assert.Equal("原名称", result.Detail.Fields["ENAME"]);
        }
        Assert.Equal(1, buttons.Checks);
        Assert.Equal(1, repository.Reads);
        Assert.Equal(denied ? 0 : 1, repository.Writes);
        Assert.Equal(!denied, transaction.Committed);
        Assert.False(transaction.IsActive);
    }

    private sealed class Buttons(Transaction transaction, string operation, bool denied) : IButtonExecutionAccess
    {
        internal int Checks;
        public ButtonExecutionContext Bind(string resourceKey, string objectName, string actualOperation, string? objectId)
        {
            Assert.Equal(operation, actualOperation);
            return new("formal-state-button", "formal-state-position", resourceKey + "." + actualOperation, resourceKey, objectName, objectId);
        }
        public async Task<T> ExecuteAsync<T>(ButtonExecutionContext context, ButtonBusinessTarget target, Func<CancellationToken, Task<T>> execute, CancellationToken token = default)
        {
            Assert.True(transaction.IsActive);
            Assert.Equal("test.item." + operation, context.BusinessBinding);
            var before = Assert.IsType<ButtonExecutionTarget>(target);
            Assert.Equal("Item_official", before.ObjectId);
            Assert.Equal("原名称", before.Before["ENAME"]);
            Assert.False((bool)before.Before["IsEnabled"]!);
            Checks++;
            if (denied) throw new ActionDataAccessException(403, "DataScope.ActionDenied");
            return await execute(token);
        }
        public ButtonBusinessInvocation BindHandler(Type handlerType) => throw new NotSupportedException();
        public Task<T> ExecuteAsync<T>(ButtonBusinessInvocation invocation, ButtonBusinessTarget? target, Func<CancellationToken, Task<T>> execute, CancellationToken token = default) => throw new NotSupportedException();
    }

    private sealed class Repository(Transaction transaction) : InMemoryStandardCrudRepository([]), IStandardCrudUpdateParticipant
    {
        internal int Reads;
        internal int Writes;
        public Task<IReadOnlyDictionary<string, object?>?> ReadForUpdateAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token = default)
        {
            Assert.True(transaction.IsActive);
            Reads++;
            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(new Dictionary<string, object?> { ["EID"] = id, ["ENAME"] = "原名称", ["IsEnabled"] = false });
        }
        public Task<IReadOnlyDictionary<string, object?>> RegisterUpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken token = default)
        {
            Assert.True(transaction.IsActive);
            Assert.Equal(new[] { "IsEnabled" }, values.Keys);
            Writes++;
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>(values) { ["EID"] = id, ["ENAME"] = "原名称" });
        }
    }

    private sealed class Transaction : IPlatformTransactionExecutor
    {
        public bool IsActive { get; private set; }
        internal bool Committed;
        public async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> execute, CancellationToken token = default)
        {
            IsActive = true;
            try { var result = await execute(token); Committed = true; return result; }
            finally { IsActive = false; }
        }
    }
}
