using System.Runtime.CompilerServices;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Testing;

/// <summary>字段单元测试的显式无对象要求按钮；身份与规范目标仍经过真实按钮执行器。</summary>
internal static class StandardCrudFieldFixture
{
    private static readonly ConditionalWeakTable<StandardCrudDefaultWriter, IButtonExecutionAccess> Buttons = new();

    internal static StandardCrudDefaultWriter Writer(StandardCrudTestAccess unusedReads, IStandardCrudRepository repository,
        IEnumerable<IStandardCrudValidator> validators, IEnumerable<IStandardCrudDeleteGuard> deleteGuards,
        IEnumerable<IStandardCrudConditionalDeleteExecutor>? conditionalDeleteExecutors = null,
        IEntityAuditValueProvider? auditValueProvider = null, ITemporalInputNormalizer? temporalInputNormalizer = null,
        IEnumerable<IStandardCrudCreationRecorder>? creationRecorders = null, IPlatformTransactionExecutor? transactionExecutor = null,
        IEnumerable<IStandardCrudUpdateRecorder>? updateRecorders = null, IPlatformObjectIdGenerator? objectIds = null,
        ICP.Platform.Crud.Ownership.EntityOwnershipValues? ownership = null, ICP.Platform.Crud.Ownership.EntityOwnershipAudit? ownershipAudit = null,
        IButtonExecutionAccess? buttons = null)
    {
        var selected = buttons ?? new FieldButtons();
        var writer = new StandardCrudDefaultWriter(transactionExecutor is null ? new ProtectedRepository(repository) : repository,
            validators, deleteGuards, conditionalDeleteExecutors: conditionalDeleteExecutors, auditValueProvider: auditValueProvider,
            temporalInputNormalizer: temporalInputNormalizer, creationRecorders: creationRecorders,
            transactionExecutor: transactionExecutor ?? new FieldTransaction(), updateRecorders: updateRecorders,
            objectIds: objectIds, ownership: ownership, ownershipAudit: ownershipAudit, buttons: selected);
        Buttons.Add(writer, selected);
        return writer;
    }

    internal static StandardCrudService Service(IDataExecutionAccess executions, IDataScopeEvaluationScope? evaluation,
        IStandardCrudRepository repository, IPlatformQueryExecutor queryExecutor, StandardCrudDefaultWriter defaultWriter,
        IEnumerable<IStandardCrudQueryCustomizer> queryCustomizers, DisplayValueProjection displayProjection,
        IEnumerable<IStandardCrudSummaryProvider> summaryProviders, IDataScopeObjectDecisionProvider objectScope,
        ILogger<StandardCrudService>? logger = null, IObjectOperationExecutor? objectOperations = null, IButtonExecutionAccess? buttons = null) =>
        new(executions, evaluation, repository, queryExecutor, defaultWriter, queryCustomizers, displayProjection, summaryProviders,
            objectScope, logger, objectOperations, buttons ?? Buttons.GetValue(defaultWriter, _ => throw new InvalidOperationException("Field fixture writer required.")));

    /// <summary>候选测试显式声明create；未配置对象要求的字段测试不会调用允许替身。</summary>
    internal sealed class FieldButtons(IDataScopeObjectDecisionProvider? candidates = null) : IButtonExecutionAccess
    {
        private readonly Dictionary<ButtonExecutionContext, ButtonExecutionAccess> sessions = [];

        public ButtonExecutionContext Bind(string resourceKey, string objectName, string operation, string? objectId)
        {
            var session = Session(resourceKey, objectName, operation);
            var context = session.Bind(resourceKey, objectName, operation, objectId);
            sessions[context] = session;
            return context;
        }

        public Task<T> ExecuteAsync<T>(ButtonExecutionContext context, ButtonBusinessTarget target,
            Func<CancellationToken, Task<T>> execute, CancellationToken token = default) =>
            sessions.TryGetValue(context, out var session) ? session.ExecuteAsync(context, target, execute, token)
                : throw new InvalidOperationException("Field fixture button was not bound.");
        public ButtonBusinessInvocation BindHandler(Type handlerType) => throw new NotSupportedException();
        public Task<T> ExecuteAsync<T>(ButtonBusinessInvocation invocation, ButtonBusinessTarget? target,
            Func<CancellationToken, Task<T>> execute, CancellationToken token = default) => throw new NotSupportedException();

        private ButtonExecutionAccess Session(string resourceKey, string objectName, string operation)
        {
            if (operation is not ("create" or "update" or "delete" or "enable" or "disable")) throw new NotSupportedException();
            var binding = resourceKey + "." + operation;
            var declaration = new ActionDataAccessDeclaration("Fields", resourceKey, "rowActions", operation, "", [resourceKey],
                candidates is null ? [] : ["create"]) { BusinessBinding = binding, Object = candidates is null ? null : objectName };
            var module = new CanonicalModulePlanModule("Fields", "1.0", "Fields", null, "", null, null, null, null, "", [], [], [], [], [], new("test", "test", "/"))
            {
                Features = [new("Fields", "fields", "navigation", "Fields", null, resourceKey + ".read"),
                    new("Fields", operation, "action", operation, "fields", binding) { DataResources = [resourceKey], DataAccess = [declaration] }],
                DataActions = [new("Fields", "create", "Create", "data.create")]
            };
            var plan = new CanonicalModulePlan("fields", "1.0", "", "", [], [module]);
            var button = plan.ActionDataAccessCatalog.Single();
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "field-fixture")], "field-fixture")) };
            http.Request.Headers["X-NewP-Feature"] = button.FeaturePositions.Single();
            http.Request.Headers["X-NewP-Button"] = button.Id;
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EndpointModuleAttribute("Fields"),
                new RequireFeatureAttribute(binding), new ButtonExecutionEndpoint(binding, resourceKey, objectName)), "field fixture"));
            return new(plan, new HttpContextAccessor { HttpContext = http }, new ResourceCatalog(resourceKey, objectName), new CandidateActions(candidates, resourceKey));
        }
    }

    private sealed class ResourceCatalog(string resourceKey, string objectName) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new(resourceKey, "Fields", objectName, "EID")];
    }

    private sealed class CandidateActions(IDataScopeObjectDecisionProvider? candidates, string resourceKey) : IObjectActionAccess
    {
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions, CancellationToken token = default) => throw new InvalidOperationException("Field fixture has no existing-object requirements.");
        public async Task<DataExecutionDecision> CheckCandidateAsync(string objectName, IReadOnlyDictionary<string, object?> candidate,
            IReadOnlyList<string> actions, CancellationToken token = default)
        {
            if (candidates is null || !actions.SequenceEqual(["create"])) throw new InvalidOperationException("Candidate requirement was not declared.");
            return await candidates.IsCandidateAllowedAsync(resourceKey, candidate, "create", token) ? DataExecutionDecision.Allowed : DataExecutionDecision.Denied;
        }
    }

    // 此测试事务只限定内存字段测试的调用期，不证明数据库锁、回滚或原子性；这些由真实SQL消费者覆盖。
    internal sealed class FieldTransaction : IPlatformTransactionExecutor
    {
        public bool IsActive { get; private set; }
        public async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> changes, CancellationToken token = default)
        {
            if (IsActive) return await changes(token);
            IsActive = true;
            try { return await changes(token); }
            finally { IsActive = false; }
        }
    }

    // 复用原观察仓储，不把其内容换成另一套数据；只补当前字段消费者必需的受保护接口。
    private sealed class ProtectedRepository(IStandardCrudRepository inner) : IStandardCrudRepository, IStandardCrudUpdateParticipant
    {
        private readonly Dictionary<string, IReadOnlyDictionary<string, object?>> before = [];
        public async Task<IReadOnlyDictionary<string, object?>?> ReadForUpdateAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token = default)
        {
            var row = await inner.GetByIdAsync(descriptor, id, token);
            if (row is not null) before[id] = row;
            return row;
        }
        public async Task<IReadOnlyDictionary<string, object?>> RegisterUpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken token = default) =>
            (descriptor.ConcurrencyRevisionField is { } revision
                ? await inner.UpdateAsync(descriptor, id, values, Convert.ToInt64(before[id][revision]), token)
                : await inner.UpdateAsync(descriptor, id, values, token))!;
        public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token = default) => inner.GetByIdAsync(descriptor, id, token);
        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken token = default) => inner.OptionsAsync(descriptor, query, token);
        public Task<StandardCrudLookupResult> LookupAsync(StandardCrudDescriptor descriptor, StandardCrudLookupRequest request, CancellationToken token = default) => inner.LookupAsync(descriptor, request, token);
        public Task<bool> ExistsAsync(StandardCrudDescriptor descriptor, StandardCrudUniqueConstraint constraint, IReadOnlyDictionary<string, object?> values, string? excludedId = null, CancellationToken token = default) => inner.ExistsAsync(descriptor, constraint, values, excludedId, token);
        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken token = default) => inner.CreateAsync(descriptor, values, token);
        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken token = default) => inner.UpdateAsync(descriptor, id, values, token);
        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, long revision, CancellationToken token = default) => inner.UpdateAsync(descriptor, id, values, revision, token);
        public Task<bool> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token = default) => inner.DeleteAsync(descriptor, id, token);
    }
}
