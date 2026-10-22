using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipFailureSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "三模型补齐登记、写入、保存、提交失败及真实请求取消共同回滚")]
    public async Task Batch_AuditFailuresAndCancellationRollBackAllModels_ThenRetrySucceeds()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContainerAsync(fixture, admin);
        var targets = new List<Target>();
        foreach (var (model, kind, action) in Kinds)
        {
            var target = await CreateAsync(fixture, admin, position, model, kind, action, "rollback-" + model);
            await ClearOwnershipAsync(fixture, target);
            targets.Add(target);
        }
        var before = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var target in targets) before[target.Id] = await RowAsync(fixture, target);
        var point = Point(fixture, "workspace.folder.initializeOwnership");
        var body = new { position, linkIds = targets.Select(target => target.LinkId).ToArray() };
        foreach (var mode in new[] { "register", "write", "save", "commit" })
        {
            fixture.Fault.Mode = mode;
            var elapsed = Stopwatch.StartNew();
            try
            {
                // 入口只翻译归属业务失败；真实基础设施故障保持原异常，不伪装为成功或拒绝。
                var failure = await Record.ExceptionAsync(async () =>
                {
                    using var response = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/ownership/initialize", body, OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                });
                if (failure is not null) Assert.StartsWith("injected ", Assert.IsType<InvalidOperationException>(failure).Message, StringComparison.Ordinal);
            }
            finally { fixture.Fault.Mode = ""; }
            await AssertUnchangedAsync(fixture, targets, before);
            output.WriteLine($"ownership rollback mode={mode} objects=3 elapsedMs={elapsed.Elapsed.TotalMilliseconds:F2}");
        }
        using var cancellation = new CancellationTokenSource();
        var registrations = 0;
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType != "entity.ownership.backfilled" || ++registrations != 2) return;
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        var cancelElapsed = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/ownership/initialize") { Content = JsonContent.Create(body) };
            request.Headers.Add("X-NewP-Feature", point.Position);
            request.Headers.Add("X-NewP-Button", OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point).Id);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admin.SendAsync(request, cancellation.Token));
        }
        finally { fixture.Fault.AfterRegister = null; }
        Assert.Equal(2, registrations);
        await AssertUnchangedAsync(fixture, targets, before);
        output.WriteLine($"ownership cancellation objects=3 updatedBeforeCancellation=2 elapsedMs={cancelElapsed.Elapsed.TotalMilliseconds:F2}");
        using var retry = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/ownership/initialize", body, OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", point));
        Assert.Equal(3, (await ReadOkAsync(retry)).GetProperty("changed").GetInt32());
    }

    internal static async Task ClearOwnershipAsync(BusinessAuditHostFixture fixture, Target target)
    {
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(target.Model)
            .Where(row => EF.Property<string>(row, "EID") == target.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => EF.Property<string?>(row, "OwnerId"), (string?)null)
                .SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), (string?)null));
    }

    private static async Task AssertUnchangedAsync(BusinessAuditHostFixture fixture, IEnumerable<Target> targets,
        IReadOnlyDictionary<string, Dictionary<string, object?>> before)
    {
        foreach (var target in targets)
        {
            var current = await RowAsync(fixture, target);
            foreach (var name in new[] { "OwnerId", "OwnerDepartmentId", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
                Assert.Equal(before[target.Id][name], current[name]);
        }
        using var scope = fixture.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
            .AnyAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.backfilled"));
    }
}
