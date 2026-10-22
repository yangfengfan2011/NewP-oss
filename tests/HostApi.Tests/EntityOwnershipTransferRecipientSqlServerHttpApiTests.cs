using System.Data.Common;
using System.Net;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;
using static ICP.Platform.HostApi.Tests.EntityOwnershipTransferSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferRecipientSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "请求已开始后、最终账号事实读取前完成删除，转交不得使用旧候选并且归属/引用/通知无部分事实")]
    public async Task RecipientDeletedBeforeFinalFactRead_RejectsTheStartedTransferWithoutPartialFacts()
    {
        var probe = new RecipientReadBarrier();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(probe)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var transfer = Point(fixture, "workspace.folder.transfer");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ConcurrentTransferOwner", "并发转交所属人",
            "concurrent-transfer-owner", "所属人", Point(fixture, "workspace.spaces.access"), transfer, Point(fixture, "workspace.folder.createPart"));
        using var client = owner.Client;
        var target = await CreateAsync(fixture, client, position, "Part", "parts", "createPart", "DELETE-RECIPIENT-TARGET");
        var http = new FeatureHttpTestRequests(fixture);
        var created = await http.CreateButtonAsync(admin, http.Identity("create"), "/api/identity/accounts",
            new { ECODE = "delete-before-transfer-read", ENAME = "将被删除的接收账号" });
        var recipient = created.GetProperty("account");
        var recipientId = recipient.GetProperty("eid").GetString()!;
        using (var scope = fixture.Services.CreateScope())
            probe.Arm(scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Model.FindEntityType("IdentityAccount")!.GetTableName()!, recipientId);
        var models = new[] { "Part", "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "WorkspaceTransferNotification" };
        var before = await SnapshotAsync(fixture, models);
        var pending = FeatureHttpTestRequests.SendAsync(client, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer",
            new { position, linkIds = new[] { target.LinkId }, recipientId }, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer));
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var deleted = await http.SendButtonAsync(admin, http.Identity("delete"), HttpMethod.Post,
                $"/api/identity/accounts/{recipientId}/delete", new { ExpectedRevision = recipient.GetProperty("revision").GetInt64(), Reason = "在最终接收事实读取前提交删除" });
            await ReadOkAsync(deleted);
            using (var confirmation = fixture.Services.CreateScope())
                Assert.Null(await confirmation.ServiceProvider.GetRequiredService<IIdentityAccountDirectory>().FindAsync(recipientId));
            probe.Release.TrySetResult();
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("EntityOwnership.RecipientUnavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            probe.Release.TrySetResult();
            if (!pending.IsCompleted) (await pending.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
        }
        Assert.Equal(before, await SnapshotAsync(fixture, models));
        using var final = fixture.Services.CreateScope();
        Assert.False(await final.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
            .AnyAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.transferred"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "转交请求开始后最终可信事实前提交人员调动，归属及接收目录使用当前部门而非旧候选")]
    public async Task RecipientDepartmentChangedBeforeFinalFactRead_UsesTheCommittedCurrentDepartment()
    {
        var probe = new RecipientReadBarrier();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(probe)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var read = Point(fixture, "workspace.spaces.access"); var transfer = Point(fixture, "workspace.folder.transfer");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "MovingRecipientOwner", "转交人",
            "moving-recipient-owner", "转交人", read, transfer, Point(fixture, "workspace.folder.createPart"));
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "MovingTransferRecipient", "调动接收人",
            "moving-transfer-recipient", "调动接收人", read);
        using var ownerClient = owner.Client; using var recipientClient = recipient.Client;
        var target = await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "MOVING-RECIPIENT-TARGET");
        var original = await RowAsync(fixture, target);
        string personId; string companyId; string oldDepartment; string accountTable;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var person = await db.Set<Dictionary<string, object?>>("Person").AsNoTracking().SingleAsync(row => EF.Property<string?>(row, "AccountEID") == recipient.AccountId);
            personId = (string)person["EID"]!; companyId = (string)person["CompanyId"]!; oldDepartment = (string)person["DepartmentId"]!;
            accountTable = db.Model.FindEntityType("IdentityAccount")!.GetTableName()!;
        }
        var http = new FeatureHttpTestRequests(fixture);
        var created = await http.CreateButtonAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "TRANSFER-CURRENT-DEPT", ENAME = "提交时当前部门", CompanyId = companyId, SortOrder = "1" });
        var newDepartment = BusinessAuditSqlServerHttpApiTests.Id(created);
        Assert.NotEqual(oldDepartment, newDepartment);
        probe.Arm(accountTable, recipient.AccountId);
        var pending = FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer",
            new { position, linkIds = new[] { target.LinkId }, recipientId = recipient.AccountId }, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer));
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using (var moved = await http.SendButtonAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
                       "/api/organization/persons/" + personId, new { CompanyId = companyId, DepartmentId = newDepartment }))
                await ReadOkAsync(moved);
            using (var confirmation = fixture.Services.CreateScope())
                Assert.Equal(newDepartment, (await confirmation.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                    .Set<Dictionary<string, object?>>("Person").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == personId))["DepartmentId"]);
            probe.Release.TrySetResult();
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, (await ReadOkAsync(response)).GetProperty("changed").GetInt32());
        }
        finally
        {
            probe.Release.TrySetResult();
            if (!pending.IsCompleted) (await pending.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
        }
        var current = await RowAsync(fixture, target);
        Assert.Equal(recipient.AccountId, current["OwnerId"]); Assert.Equal(newDepartment, current["OwnerDepartmentId"]);
        Assert.Equal(original["CreatedBy"], current["CreatedBy"]); Assert.Equal(original["CreatedAt"], current["CreatedAt"]);
        using var final = fixture.Services.CreateScope();
        var receivingFolders = await final.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking()
            .Where(row => EF.Property<string?>(row, "OwnerId") == recipient.AccountId).ToArrayAsync();
        Assert.Equal(2, receivingFolders.Length);
        Assert.All(receivingFolders, folder => Assert.Equal(newDepartment, folder["OwnerDepartmentId"]));
    }

    // 真实最终账号SELECT尚未执行时暂停一次；删除事务自己随后可读取该账号，不靠延时猜测交错。
    private sealed class RecipientReadBarrier : DbCommandInterceptor
    {
        private string table = "";
        private string account = "";
        private int armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm(string tableName, string accountId) { table = "[" + tableName + "]"; account = accountId; Interlocked.Exchange(ref armed, 1); }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(table, StringComparison.Ordinal) && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, account))
                && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
