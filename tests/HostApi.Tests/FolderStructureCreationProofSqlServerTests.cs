using System.Data.Common;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class FolderStructureParentAuthorizationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Added创建图捕获后允许Flush及当前修改，伪事实、替换Entry、后加入和跨scope/事务均零SQL拒绝")]
    public async Task StructureCreationProof_UsesOnlyCapturedEntriesAndExpiresWithItsOriginalScopeAndTransaction()
    {
        var commands = new CreationProofCommands();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var transactions = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
        var initializer = scope.ServiceProvider.GetRequiredService<IDataScopeObjectInitializer>();
        var sources = scope.ServiceProvider.GetRequiredService<DataScopeObjectInitializationSources>();
        var template = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking().FirstAsync();
        var externalParent = (string)template["EID"]!;
        var first = NewFolder("capture-first");
        var second = NewFolder("capture-second");
        DataScopeObjectCreationProof? proof = null;

        // 捕获时必须仍是真实Added；随后实际Flush产生Unchanged，当前字段修改不替换参与行身份。
        await Assert.ThrowsAsync<CreationProofRollback>(() => transactions.ExecuteAsync<bool>(async token =>
        {
            db.Set<Dictionary<string, object?>>("WorkspaceFolder").Attach(template);
            db.Set<Dictionary<string, object?>>("WorkspaceFolder").AddRange(first, second);
            commands.Active = true;
            proof = initializer.CaptureCreatedObjects("workspace.folder", [Facts(first), Facts(second)]);
            Assert.Equal(0, commands.Count);
            commands.Active = false;
            await transactions.FlushAsync(token);
            Assert.Equal(EntityState.Unchanged, db.Entry(first).State);
            second["Description"] = "same created row after flush";
            db.Entry(second).State = EntityState.Modified;
            commands.Active = true;
            var firstFacts = Facts(first);
            var secondFacts = Facts(second);
            var rootSource = new DataScopeObjectInitializationSource.Created([externalParent], firstFacts) { Proof = proof };
            var childSource = new DataScopeObjectInitializationSource.CreatedFromCreatedParents([firstFacts], secondFacts, proof);
            sources.Validate("workspace.folder", [externalParent], firstFacts.ObjectId, rootSource);
            sources.Validate("workspace.folder", [firstFacts.ObjectId], secondFacts.ObjectId, childSource);
            Assert.Equal(0, commands.Count);
            Invalid(() => sources.Validate("workspace.folder", [externalParent], firstFacts.ObjectId, rootSource with { Proof = null }));
            Invalid(() => sources.Validate("workspace.folder", [externalParent], firstFacts.ObjectId, rootSource with { Proof = new UnknownCreationProof() }));

            // 已存在行不能在捕获时冒充新建；字段相同也不能代替原捕获Entry或扩大创建参与集合。
            Invalid(() => initializer.CaptureCreatedObjects("workspace.folder", [Facts(template)]));
            Invalid(() => initializer.CaptureCreatedObjects("workspace.folder", [secondFacts]));
            Invalid(() => sources.Validate("workspace.folder", [externalParent], firstFacts.ObjectId,
                rootSource with { Target = firstFacts with { Values = new Dictionary<string, object?>(firstFacts.Values) { ["ENAME"] = "forged" } } }));
            var later = NewFolder("capture-later");
            db.Set<Dictionary<string, object?>>("WorkspaceFolder").Add(later);
            var laterFacts = Facts(later);
            Invalid(() => initializer.CaptureCreatedObjects("workspace.folder",
                [laterFacts with { Values = new Dictionary<string, object?>(laterFacts.Values) { ["ENAME"] = "forged" } }]));
            Invalid(() => sources.Validate("workspace.folder", [externalParent], laterFacts.ObjectId,
                new DataScopeObjectInitializationSource.Created([externalParent], laterFacts) { Proof = proof }));
            Invalid(() => sources.Validate("workspace.folder", [laterFacts.ObjectId], secondFacts.ObjectId,
                new DataScopeObjectInitializationSource.CreatedFromCreatedParents([laterFacts], secondFacts, proof)));

            // 同DbContext和活动事务也不能借给另一个Sources实例，证明归原请求scope拥有。
            var otherScopeSources = ActivatorUtilities.CreateInstance<DataScopeObjectInitializationSources>(scope.ServiceProvider);
            Invalid(() => otherScopeSources.Validate("workspace.folder", [externalParent], firstFacts.ObjectId, rootSource));
            db.Entry(first).State = EntityState.Detached;
            var replacement = new Dictionary<string, object?>(first);
            db.Set<Dictionary<string, object?>>("WorkspaceFolder").Attach(replacement);
            Invalid(() => sources.Validate("workspace.folder", [externalParent], firstFacts.ObjectId, rootSource));
            Assert.Equal(0, commands.Count);
            commands.Active = false;
            throw new CreationProofRollback();
        }).AsTask());

        Assert.NotNull(proof);
        db.ChangeTracker.Clear();
        commands.Active = true;
        var expiredFacts = Facts(second);
        var expired = new DataScopeObjectInitializationSource.Created([externalParent], expiredFacts) { Proof = proof };
        Invalid(() => sources.Validate("workspace.folder", [externalParent], expiredFacts.ObjectId, expired));
        await transactions.ExecuteAsync<bool>(_ =>
        {
            Invalid(() => sources.Validate("workspace.folder", [externalParent], expiredFacts.ObjectId, expired));
            return ValueTask.FromResult(true);
        });
        Assert.Equal(0, commands.Count);
        commands.Active = false;

        Dictionary<string, object?> NewFolder(string name) => new(template, StringComparer.Ordinal)
        { ["EID"] = "WorkspaceFolder_" + Guid.NewGuid().ToString("N"), ["ENAME"] = name };
        static DataExecutionWriteFacts Facts(Dictionary<string, object?> row) => new("workspace.folder", (string)row["EID"]!, new Dictionary<string, object?>(row));
        static void Invalid(Action validate)
        {
            var failure = Assert.Throws<ActionDataAccessException>(validate);
            Assert.Equal("ActionDataAccess.InitializationSourceInvalid", failure.Code);
        }
    }

    private sealed class CreationProofRollback : Exception { }
    private sealed class UnknownCreationProof : DataScopeObjectCreationProof { }

    private sealed class CreationProofCommands : DbCommandInterceptor
    {
        internal bool Active;
        internal int Count;
        private void Observe() { if (Active) Count++; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        { Observe(); return result; }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        { Observe(); return result; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
        { Observe(); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Observe(); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Observe(); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken token = default)
        { Observe(); return ValueTask.FromResult(result); }
    }
}
