using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Planning;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.Tests;

// 两个字段HTTP夹具复用真实EF事务和受保护行；仅观测成功写入，判权安全性由正式包SQL证明。
internal class FieldHttpEfRepository : IStandardCrudRepository, IStandardCrudUpdateParticipant
{
    private EfStandardCrudRepository _inner = null!;
    private EfPlatformDbContext _db = null!;
    private StandardCrudDescriptor _descriptor = null!;
    internal List<Dictionary<string, object?>> Rows { get; } = [];
    internal int CreateCallCount { get; private set; }
    internal int UpdateCallCount { get; private set; }

    internal static CanonicalModulePlan ForModule(CanonicalModulePlan original, string module) => new(
        original.CandidateId, original.CompilerContractVersion, original.PlanHash, original.SnapshotId, original.CanonicalBytes,
        original.Modules.Select(item => item.ModuleId == module ? item : item with { Features = [], DataExecutions = [] }).ToArray());

    internal static void Configure(IServiceCollection services, CanonicalModulePlan plan, StandardCrudDescriptor descriptor,
        ModelDefinition model, FieldHttpEfRepository observer, string subject)
    {
        if (!plan.Modules.Any(module => module.ModuleId == descriptor.ModuleId)) throw new InvalidOperationException("字段夹具缺实际资源owner。");
        var persistence = new CompiledPersistencePlan("field-http", "field-http", "field-http", default, "field-http", [], [model], []);
        services.AddSingleton(persistence);
        services.AddSingleton<ICP.Platform.Presentation.ITemporalDisplayProjectionExecutor>(
            new ICP.Platform.Presentation.TemporalDisplayProjectionExecutor(new FieldPresentation(), new ICP.Platform.Presentation.TemporalDisplayFormatter()));
        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        });
        // 请求在同一夹具内串行；单连接的生命期随WebApplication结束，不替换正式事务实现。
        services.AddSingleton(provider => new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlite(provider.GetRequiredService<SqliteConnection>())
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options,
            persistence, provider.GetRequiredService<ICP.Platform.Persistence.Auditing.IEntityAuditValueProvider>()));
        services.AddSingleton<IPlatformTransactionExecutor, EfPlatformTransactionExecutor>();
        services.AddSingleton<IStandardCrudRepository>(observer);
        services.AddSingleton<IDataScopeResourceCatalog, DataScopeResourceCatalog>();
        services.AddScoped<IObjectActionAccess, ObjectActionAccess>();
        services.AddScoped<IButtonExecutionAccess, ButtonExecutionAccess>();
        services.AddScoped<IButtonBusinessExecutor, ButtonBusinessExecutor>();
        services.AddScoped<DataExecutionExceptionFilter>();
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<DataScopeObjectManagement>(provider,
            provider.GetRequiredService<IDataExecutionAccess>()));
        services.AddScoped<IDataScopeObjectReadHandler>(provider => provider.GetRequiredService<DataScopeObjectManagement>());
        services.AddScoped<DataScopeObjectInspectionHandler>();
        services.AddSingleton(provider => new FieldDecisions(provider.GetRequiredService<IHttpContextAccessor>(), descriptor, subject));
        services.AddSingleton<IDataScopeObjectAccess>(provider => provider.GetRequiredService<FieldDecisions>());
        services.AddSingleton<IDataScopeObjectDecisionProvider>(provider => provider.GetRequiredService<FieldDecisions>());
        services.AddSingleton<IDataScopeReadPlanProvider>(provider => provider.GetRequiredService<FieldDecisions>());
        observer._descriptor = descriptor;
    }

    internal void Attach(IServiceProvider services)
    {
        _db = services.GetRequiredService<EfPlatformDbContext>();
        _db.Database.EnsureCreated();
        foreach (var row in Rows) _db.Set<Dictionary<string, object?>>(_descriptor.ModelName).Add(row);
        if (Rows.Count > 0) _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _inner = services.GetRequiredService<EfStandardCrudRepository>();
    }

    internal async Task CorruptStoredFieldAsync(string id, string field, object? value)
    {
        var row = await _db.Set<Dictionary<string, object?>>(_descriptor.ModelName)
            .SingleAsync(item => EF.Property<string>(item, _descriptor.IdField) == id);
        row[field] = value;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        Observe(row);
    }

    public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token = default) =>
        _inner.GetByIdAsync(descriptor, id, token);
    public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken token = default) =>
        _inner.OptionsAsync(descriptor, query, token);
    public Task<bool> ExistsAsync(StandardCrudDescriptor descriptor, StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> values, string? excludedId = null, CancellationToken token = default) =>
        _inner.ExistsAsync(descriptor, constraint, values, excludedId, token);
    public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values, CancellationToken token = default)
    {
        var row = await _inner.CreateAsync(descriptor, values, token);
        CreateCallCount++;
        Observe(row);
        return row;
    }
    public Task<IReadOnlyDictionary<string, object?>?> ReadForUpdateAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token = default) =>
        _inner.ReadForUpdateAsync(descriptor, id, token);
    public async Task<IReadOnlyDictionary<string, object?>> RegisterUpdateAsync(StandardCrudDescriptor descriptor, string id,
        IReadOnlyDictionary<string, object?> values, CancellationToken token = default)
    {
        var row = await _inner.RegisterUpdateAsync(descriptor, id, values, token);
        UpdateCallCount++;
        Observe(row);
        return row;
    }
    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(StandardCrudDescriptor descriptor, string id,
        IReadOnlyDictionary<string, object?> values, CancellationToken token = default)
    {
        var row = await _inner.UpdateAsync(descriptor, id, values, token);
        if (row is not null) { UpdateCallCount++; Observe(row); }
        return row;
    }
    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(StandardCrudDescriptor descriptor, string id,
        IReadOnlyDictionary<string, object?> values, long expectedRevision, CancellationToken token = default)
    {
        var row = await _inner.UpdateAsync(descriptor, id, values, expectedRevision, token);
        if (row is not null) { UpdateCallCount++; Observe(row); }
        return row;
    }
    public Task<bool> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken token = default) =>
        _inner.DeleteAsync(descriptor, id, token);

    private void Observe(IReadOnlyDictionary<string, object?> values)
    {
        var row = Rows.SingleOrDefault(item => Equals(item[_descriptor.IdField], values[_descriptor.IdField]));
        if (row is null) { row = new(StringComparer.Ordinal); Rows.Add(row); }
        foreach (var pair in values) row[pair.Key] = pair.Value;
    }

    private sealed class FieldPresentation : ICP.Platform.Presentation.IRequestPresentationContextAccessor
    {
        public ICP.Platform.Presentation.RequestPresentationContext? Current => new("zh-CN", "Asia/Taipei");
    }

    // 仅本字段fixture的已声明主体/资源/动作有意见，不替换Button绑定或规范目标检查。
    private sealed class FieldDecisions(IHttpContextAccessor http, StandardCrudDescriptor descriptor, string subject)
        : IDataScopeObjectAccess, IDataScopeObjectDecisionProvider, IDataScopeReadPlanProvider
    {
        private bool Allows(string resource, string action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) == subject && resource == descriptor.ResourceKey
                && action is "read" or "create" or "edit" or "delete";
        }
        public Task<DataScopeReadPlan> GetReadPlanAsync(string resource, CancellationToken token = default) =>
            Allows(resource, "read", token) ? Task.FromResult(new DataScopeReadPlan(null))
                : throw new InvalidOperationException("字段夹具不提供该主体或资源的读取计划。");
        public Task<bool> IsReadableAsync(string resource, string id, CancellationToken token = default) => Task.FromResult(Allows(resource, "read", token));
        public Task<bool> IsAllowedAsync(string resource, string id, string action, CancellationToken token = default) => Task.FromResult(Allows(resource, action, token));
        public Task<bool> IsCandidateAllowedAsync(string resource, IReadOnlyDictionary<string, object?> candidate, string action, CancellationToken token = default) =>
            Task.FromResult(Allows(resource, action, token));
    }
}
