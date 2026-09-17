using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Crud.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationCacheSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "组织公司正式提交与缓存发布一致性")]
    public async Task CompanyApi_UpdatesCacheForEveryWriteAndDistinguishesCommittedPublicationFailure()
    {
        var admin = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true") { InitialCatalog = "master" };
        var catalog = $"NewPCompanyCache_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{catalog}]";
        await command.ExecuteNonQueryAsync();
        try
        {
            var models = OrganizationModelDefinitions.All.Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
            var plan = new CompiledPersistencePlan("candidate", "snapshot", "hash", default, "digest", [], models, []);
            var stamp = new Stamp();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddPlatformAuthorization();
            builder.Services.AddSingleton<ICP.Platform.Authorization.Http.IFeatureDecisionSource, GrantedPermissions>();
            builder.Services.AddStandardCrudKernel();
            builder.Services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
            builder.Services.AddStandardCrudConditionalDelete<CompanyCrudDeleteExecutor>();
            builder.Services.AddScoped<IPlatformTransactionExecutor, EfPlatformTransactionExecutor>();
            builder.Services.AddSingleton<OrganizationCache>();
            builder.Services.AddSingleton<ICP.Platform.Organization.Contracts.IOrganizationQuery, OrganizationQuery>();
            builder.Services.AddSingleton<ControlledParticipant>();
            builder.Services.AddSingleton(sp => new PlatformCommitRegistration("Organization/Company", sp.GetRequiredService<ControlledParticipant>()));
            builder.Services.AddScoped<ICompanyDirectory, EfCompanyDirectory>();
            var sql = new SqlCounter();
            builder.Services.AddSingleton(plan);
            builder.Services.AddSingleton<IEntityAuditStampProvider>(stamp);
            builder.Services.AddSingleton<IRequestPresentationContextAccessor, Presentation>();
            builder.Services.AddSingleton<ITemporalDisplayFormatter, TemporalDisplayFormatter>();
            builder.Services.AddSingleton<ITemporalDisplayProjectionExecutor, TemporalDisplayProjectionExecutor>();
            var database = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = catalog }.ConnectionString;
            builder.Services.AddScoped(_ => new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
                .UseSqlServer(database).AddInterceptors(sql).ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options, plan, stamp));
            builder.Services.AddScoped<IPlatformQueryExecutor, EfPlatformQueryExecutor>();
            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin")], "Test"));
                await next();
            });
            app.UseAuthorization();
            app.MapStandardCrudResources();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.EnsureCreatedAsync();
            }
            await app.Services.GetRequiredService<OrganizationCache>().InitializeAsync();
            await app.StartAsync();
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-NewP-Feature", "fixture");
            const string route = "/api/organization/companies";
            var cache = app.Services.GetRequiredService<OrganizationCache>();
            var publication = app.Services.GetRequiredService<ControlledParticipant>();
            using var created = await client.PostAsJsonAsync(route, new Dictionary<string, object?> { ["ECODE"] = "CACHE", ["ENAME"] = "缓存公司", ["SortOrder"] = "001" });
            Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("EID").GetString()!;
            var initial = await cache.ReadAsync();
            Assert.Equal("001", initial.Companies[id]["SortOrder"]);
            Assert.Equal("system", initial.Companies[id]["CreatedBy"]);
            await using (var readScope = app.Services.CreateAsyncScope())
            {
                var directory = readScope.ServiceProvider.GetRequiredService<ICompanyDirectory>();
                var beforeSql = sql.Reads;
                Assert.Equal("缓存公司", (await directory.GetByIdAsync(id))!.Name);
                Assert.Equal(id, (await directory.GetByCodeAsync("cache"))!.Id);
                Assert.Single(await directory.GetByIdsAsync([id, id]));
                Assert.Equal(beforeSql, sql.Reads);
            }
            using (var updated = await client.PutAsJsonAsync($"{route}/{id}", new Dictionary<string, object?> { ["ENAME"] = "已更新公司" }))
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal("已更新公司", (await cache.ReadAsync()).Companies[id]["ENAME"]);
            Assert.Equal("缓存公司", initial.Companies[id]["ENAME"]);

            // 自保存仓储直接调用也自动维护缓存；业务失败必须回滚真实 SQL 保存且不发布候选。
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IStandardCrudRepository>();
                var descriptor = CompanyCrudDescriptorFactory.Create();
                await repository.UpdateAsync(descriptor, id, new Dictionary<string, object?> { ["ENAME"] = "仓储直接写入" });
                Assert.Equal("仓储直接写入", (await cache.ReadAsync()).Companies[id]["ENAME"]);
                var executor = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
                await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<bool>(["Company"], async token =>
                {
                    await repository.UpdateAsync(descriptor, id, new Dictionary<string, object?> { ["ENAME"] = "应回滚" }, token);
                    throw new InvalidOperationException("rollback probe");
                }).AsTask());
            }
            Assert.Equal("仓储直接写入", (await cache.ReadAsync()).Companies[id]["ENAME"]);
            Assert.Equal("仓储直接写入", (await client.GetFromJsonAsync<JsonElement>($"{route}/{id}")).GetProperty("fields").GetProperty("ENAME").GetString());

            // 正式请求提交后暂缓发布；数据库已保存，新缓存读取等待，旧结果仍可使用。
            publication.Pause();
            var pendingWrite = client.PutAsJsonAsync($"{route}/{id}", new Dictionary<string, object?> { ["ENAME"] = "窗口提交" });
            await publication.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("窗口提交", (await client.GetFromJsonAsync<JsonElement>($"{route}/{id}")).GetProperty("fields").GetProperty("ENAME").GetString());
            var pendingRead = cache.ReadAsync().AsTask();
            Assert.False(pendingRead.IsCompleted);
            publication.Continue();
            using (var published = await pendingWrite) Assert.Equal(HttpStatusCode.OK, published.StatusCode);
            Assert.Equal("窗口提交", (await pendingRead).Companies[id]["ENAME"]);

            // 正式删除仍保留引用约束；失败结果不发布也不重试数据库变更。
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Department")).Add(new()
                { ["EID"] = "reference", ["ECODE"] = "D", ["ENAME"] = "引用部门", ["CompanyId"] = id, ["IsEnabled"] = false, ["SortOrder"] = "01" });
                await db.SaveChangesAsync();
            }
            using (var blocked = await client.DeleteAsync($"{route}/{id}")) Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.True((await cache.ReadAsync()).Companies.ContainsKey(id));
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Department")).ExecuteDeleteAsync();
            }

            // 真实 ExecuteDelete 不进入 ChangeTracker；实际影响登记仍通过同一个提交参与者发布。
            using (var deleted = await client.DeleteAsync($"{route}/{id}")) Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.False((await cache.ReadAsync()).Companies.ContainsKey(id));
            using (var missing = await client.GetAsync($"{route}/{id}")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            publication.Fail = true;
            using var committedFailure = await client.PostAsJsonAsync(route, new Dictionary<string, object?> { ["ECODE"] = "SAVED", ["ENAME"] = "已保存不可重放", ["SortOrder"] = "002" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, committedFailure.StatusCode);
            var problem = await committedFailure.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Persistence.CommittedCacheUnavailable", problem.GetProperty("code").GetString());
            Assert.Contains("已保存", problem.GetProperty("message").GetString());
            await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => cache.ReadAsync().AsTask());
            var listing = await client.GetFromJsonAsync<JsonElement>(route);
            Assert.Equal("已保存不可重放", Assert.Single(listing.GetProperty("items").EnumerateArray()).GetProperty("ENAME").GetString());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            command.CommandText = $"ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}]";
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class SqlCounter : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Reads); return ValueTask.FromResult(result); }
    }

    private sealed class ControlledParticipant(OrganizationCache cache) : IPlatformCommitParticipant
    {
        public string Name => cache.Name;
        public bool Fail;
        private bool _pause;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Pause() { _pause = true; }
        public void Continue() { _pause = false; _release.TrySetResult(); }
        public async ValueTask<IPlatformCommitScope> BeginAsync(CancellationToken token) => new Scope(this, await cache.BeginAsync(token));
        private sealed class Scope(ControlledParticipant control, IPlatformCommitScope inner) : IPlatformCommitScope
        {
            public ValueTask PrepareAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken token) => inner.PrepareAsync(changes, token);
            public void BlockReads() => inner.BlockReads();
            public async ValueTask PublishAsync()
            {
                if (control._pause) { control.Entered.TrySetResult(); await control._release.Task; }
                if (control.Fail) throw new InvalidOperationException("publication probe");
                await inner.PublishAsync();
            }
            public void MarkUnavailable() => inner.MarkUnavailable();
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
    private sealed class Stamp : IEntityAuditStampProvider
    {
        public DateTime Instant { get; set; } = new DateTime(2026, 9, 4, 7, 24, 26, DateTimeKind.Utc).AddTicks(5023291);
        public EntityAuditStamp GetCurrent() => new("system", Instant);
    }
    private sealed class Presentation : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current => new("zh-CN", "Asia/Taipei");
    }
    private sealed class GrantedPermissions : ICP.Platform.Authorization.Http.IFeatureDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string moduleId, string behaviorKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PermissionDecision.Granted);
    }
}
