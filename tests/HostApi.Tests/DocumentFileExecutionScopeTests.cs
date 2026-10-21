using ICP.Platform.Modules.Files.Contracts;
using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using ICP.Platform.Modules.Authorization.Controllers;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Document;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DocumentFileExecutionScopeTests
{
    [Fact]
    public void DownloadOwnerContext_RequiresTheActualButtonBeforeItCanBeMinted()
    {
        var (_, _, scope, _, _) = Create();
        using var phase = scope.Begin("Document_one", DocumentFileReadPhase.Open);
        Assert.Throws<ActionDataAccessException>(() => scope.DownloadContext("Document_one"));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("owner")]
    [InlineData("http")]
    [InlineData("actor")]
    [InlineData("feature")]
    [InlineData("button")]
    [InlineData("legacy")]
    [InlineData("endpoint")]
    [InlineData("ended")]
    [InlineData("nested")]
    public async Task DownloadOwnerContext_RejectsCrossedOriginAndEndedOpenBeforeReadingAssociations(string problem)
    {
        var (_, http, scope, plan, _) = Create();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "document.document.download");
        http.HttpContext!.Request.Headers["X-NewP-Button"] = declaration.Id;
        http.HttpContext.Request.Headers["X-NewP-Feature"] = Assert.Single(declaration.FeaturePositions);
        http.HttpContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute("Document"),
            new RequireFeatureAttribute("document.document.download"), new ButtonBusinessHandlerAttribute(typeof(DocumentFiles))), "download"));
        using var phase = scope.Begin("Document_one", DocumentFileReadPhase.Open);
        var context = scope.DownloadContext("Document_one");
        var consuming = problem == "owner" ? new DocumentFileReadScope(http, null!) : scope;
        if (problem == "http") http.HttpContext = Context();
        if (problem == "actor") http.HttpContext!.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "another-actor")], "test"));
        if (problem == "feature") http.HttpContext!.Request.Headers["X-NewP-Feature"] = "wrong-feature";
        if (problem == "button") http.HttpContext!.Request.Headers["X-NewP-Button"] = "another-button";
        if (problem == "legacy") http.HttpContext!.Request.Headers["X-NewP-Data-Declaration"] = declaration.Id;
        if (problem == "endpoint") http.HttpContext!.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute("Workspace")), "wrong-owner"));
        if (problem == "ended") phase.Dispose();
        using var nested = problem == "nested" ? scope.Begin("Document_two", DocumentFileReadPhase.Open) : null;
        var policy = new DocumentFileOwnerPolicy(null!, new DocumentFileCreationScope(null!), null!, null!, null!, consuming, null!, new DocumentFileCopyScope(null!, new HttpContextAccessor()));
        var operation = () => policy.VerifyBatchAsync([], FileOwnerOperation.Download, context, default);
        if (problem == "valid") await operation();
        else if (problem == "endpoint") await Assert.ThrowsAsync<ActionDataAccessException>(operation);
        else
        {
            var failure = await Assert.ThrowsAsync<FileLifecycleException>(operation);
            Assert.Equal("Document.FileReadContextInvalid", failure.Code);
        }
        if (problem == "nested")
        {
            nested!.Dispose();
            Assert.Equal("Document_one", scope.DownloadObjectId(context));
        }
        phase.Dispose();
        Assert.Null(scope.DownloadObjectId(context));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("duplicate-same-behavior")]
    [InlineData("behavior")]
    [InlineData("two-behaviors")]
    [InlineData("feature")]
    [InlineData("execution")]
    [InlineData("resource")]
    [InlineData("owner")]
    public async Task OwnEndpoint_UsesItsRealStaticFeatureAndRejectsCrossedSources(string problem)
    {
        var (_, http, _, plan, reads) = Create();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "document.document.download");
        var metadata = new List<object> { new EndpointModuleAttribute(problem == "owner" ? "Workspace" : "Document"),
            new RequireFeatureAttribute(problem == "behavior" ? "document.document.read" : "document.document.download"),
            new ButtonBusinessHandlerAttribute(typeof(DocumentFiles)) };
        if (problem == "duplicate-same-behavior") metadata.Add(new RequireFeatureAttribute("document.document.download"));
        if (problem == "two-behaviors") metadata.Add(new RequireFeatureAttribute("document.document.read"));
        http.HttpContext!.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "owned-document-download"));
        http.HttpContext.Request.Headers["X-NewP-Button"] = problem == "execution"
            ? Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.read" && item.ViewKey == "document.document.detail").Id : declaration.Id;
        http.HttpContext.Request.Headers["X-NewP-Feature"] = problem == "feature" ? "wrong-feature" : Assert.Single(declaration.FeaturePositions);
        var checks = new ButtonChecks();
        var access = new ButtonExecutionAccess(plan, http, new ResourceCatalog(false), checks);
        var target = new ButtonExecutionTarget(problem == "resource" ? "example.record" : "document.document", "Document", "Document_one",
            new Dictionary<string, object?> { ["EID"] = "Document_one" });
        var action = async () => await access.ExecuteAsync(access.BindHandler(typeof(DocumentFiles)), target, _ => Task.FromResult(true));
        if (problem == "valid") Assert.True(await action());
        else await Assert.ThrowsAsync<ActionDataAccessException>(action);
        Assert.Equal(0, reads.Calls);
        if (problem == "valid") Assert.Equal(new[] { "Document:download" }, checks.Calls);
        else Assert.Empty(checks.Calls);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("caller")]
    [InlineData("member")]
    [InlineData("resource")]
    [InlineData("eid")]
    [InlineData("source")]
    [InlineData("ended-phase")]
    [InlineData("other-phase")]
    [InlineData("http")]
    [InlineData("actor")]
    public async Task InternalCopyMetadata_AcceptsOnlyItsFixedCallerAndCurrentReadFacts(string problem)
    {
        var (access, http, scope, _, reads) = Create();
        using var phase = scope.Begin("document-one", DocumentFileReadPhase.PolicyCopyMetadata, Identity());
        var target = scope.Target("document-one");
        var binding = new DataExecutionInternalBinding("document.files.copyMetadata", "document.backend", typeof(DocumentFiles), nameof(DocumentFiles.ReadCopySourceAsync));
        if (problem == "caller") binding = binding with { Caller = typeof(DocumentFileOwnerPolicy) };
        if (problem == "member") binding = binding with { Member = nameof(DocumentFiles.OpenAsync) };
        if (problem == "resource") target = target with { ResourceKey = "example.record" };
        if (problem == "eid") target = target with { ObjectId = "another-document" };
        if (problem == "source") target = target with { Source = new object() };
        if (problem == "ended-phase") phase.Dispose();
        using var other = problem == "other-phase" ? scope.Begin("document-one", DocumentFileReadPhase.PolicyDownload, Identity()) : null;
        if (problem == "http") http.HttpContext = Context();
        if (problem == "actor") http.HttpContext!.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "another-actor")], "test"));
        if (problem == "valid") Assert.Equal(DataExecutionDecision.Allowed, (await access.EvaluateInternalObjectAsync(binding, "target", target)).Decision);
        else await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateInternalObjectAsync(binding, "target", target));
        Assert.Equal(0, reads.Calls);
    }

    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public async Task GenericManagementButton_StillRequiresItsOwnEndpointOwnerAndResourceFeature()
    {
        var (_, http, scope, plan, reads) = Create();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "example.record.authorize");
        http.HttpContext!.Request.Headers["X-NewP-Button"] = declaration.Id;
        http.HttpContext.Request.Headers["X-NewP-Feature"] = Assert.Single(declaration.FeaturePositions);
        http.HttpContext.Request.RouteValues["resourceKey"] = "example.record";
        http.HttpContext.Request.RouteValues["objectId"] = "ExampleRecord_generic";
        var controller = typeof(DataScopeObjectsController);
        var method = controller.GetMethod(nameof(DataScopeObjectsController.Get))!;
        http.HttpContext.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse(controller.GetCustomAttribute<RouteAttribute>()!.Template!), 0,
            new EndpointMetadataCollection(controller.GetCustomAttributes(true).Concat(method.GetCustomAttributes(true))), "actual-generic-controller"));
        var checks = new ButtonChecks();
        var access = new ButtonExecutionAccess(plan, http, new ResourceCatalog(false), checks);
        var target = new ButtonExecutionTarget("example.record", "ExampleRecord", "ExampleRecord_generic",
            new Dictionary<string, object?> { ["EID"] = "ExampleRecord_generic" });
        // 文件当前版本读取事实不替代generic实际对象的按钮判权。
        using var phase = scope.Begin("document-one", DocumentFileReadPhase.Metadata, Identity());
        var invocation = access.BindHandler(typeof(DataScopeObjectManagement));
        Assert.True(await access.ExecuteAsync(invocation, target, _ => Task.FromResult(true)));
        Assert.Equal(new[] { "ExampleRecord:authorize" }, checks.Calls); Assert.Equal(0, reads.Calls);
        http.HttpContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute("Example"),
            new RequireFeatureAttribute("example.record.update"), new ButtonBusinessHandlerAttribute(typeof(DataScopeObjectManagement))), "wrong-owned-endpoint"));
        Assert.Throws<ActionDataAccessException>(() => access.BindHandler(typeof(DataScopeObjectManagement)));
        Assert.Single(checks.Calls);
    }

    [Fact]
    public async Task InternalCopyRead_RejectsAnArbitraryImplementationIdentityBeforeAnyQuery()
    {
        // 未构造后续基础设施，证明明确caller守卫在任何查询之前拒绝。
        var policy = new DocumentFileOwnerPolicy(null!, new DocumentFileCreationScope(null!), null!, Microsoft.Extensions.Options.Options.Create(new DocumentOptions()), null!, null!, null!, new DocumentFileCopyScope(null!, new HttpContextAccessor()));
        var source = new FileOwnerInternalExecutionContext(new("document.files.copySource", "document.files.copySource", typeof(IFileCopyService), "PrepareCopyAsync"), "document.document", "document-one");
        var failure = await Assert.ThrowsAsync<ICP.Platform.Modules.Files.Contracts.FileLifecycleException>(() => policy.VerifyBatchAsync([], ICP.Platform.Modules.Files.Contracts.FileOwnerOperation.CopySource, source, default));
        Assert.Equal("Document.FileReadContextInvalid", failure.Code);
    }

    [Theory]
    [InlineData("PrepareCopyFilesAsync", true)]
    [InlineData("CopyAsync", true)]
    [InlineData("CancelCopyAsync", false)]
    [InlineData("ReadCopyAsync", false)]
    public async Task InternalCopySource_AcceptsOnlyItsTwoActualSourceReadingMembers(string member, bool allowed)
    {
        var policy = new DocumentFileOwnerPolicy(null!, new DocumentFileCreationScope(null!), null!, null!, null!, null!, null!, new DocumentFileCopyScope(null!, new HttpContextAccessor()));
        var source = new FileOwnerInternalExecutionContext(new("document.files.copySource", "document.files.copySource", typeof(DocumentFiles), member), "document.document", "document-one");
        if (allowed) await policy.VerifyBatchAsync([], FileOwnerOperation.CopySource, source, default);
        else
        {
            var failure = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyBatchAsync([], FileOwnerOperation.CopySource, source, default));
            Assert.Equal("Document.FileReadContextInvalid", failure.Code);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("execution")]
    [InlineData("provider")]
    [InlineData("resource")]
    [InlineData("object")]
    [InlineData("caller")]
    [InlineData("member")]
    public async Task CopySource_RejectsCrossedContextsBeforeTheEmptyBatchCanReturn(string problem)
    {
        var source = DocumentCopyExecutions.Files("document-one", nameof(DocumentFiles.CopyAsync));
        FileOwnerOperationContext? crossed = problem switch
        {
            "missing" => null,
            "execution" => source with { Source = source.Source with { Execution = "document.files.download" } },
            "provider" => source with { Source = source.Source with { Provider = "document.backend" } },
            "resource" => source with { ResourceKey = "part.part" },
            "object" => source with { ObjectId = "" },
            "caller" => source with { Source = source.Source with { Caller = typeof(IFileCopyService) } },
            _ => source with { Source = source.Source with { Member = "ReadCopyAsync" } }
        };
        // 没有任何DB或evaluator；guard必须在空批返回及任何读取前拒绝串用来源。
        var policy = new DocumentFileOwnerPolicy(null!, new DocumentFileCreationScope(null!), null!, null!, null!, null!, null!, new DocumentFileCopyScope(null!, new HttpContextAccessor()));
        var failure = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyBatchAsync([], FileOwnerOperation.CopySource, crossed, default));
        Assert.Equal("Document.FileReadContextInvalid", failure.Code);
    }

    [Fact]
    public async Task CopyConfirmation_DoesNotBorrowThePrepareSourceIdentity()
    {
        var policy = new DocumentFileOwnerPolicy(null!, new DocumentFileCreationScope(null!), null!, null!, null!, null!, null!, new DocumentFileCopyScope(null!, new HttpContextAccessor()));
        var source = new FileOwnerInternalExecutionContext(new("document.files.copySource", "document.files.copySource", typeof(DocumentFiles), nameof(DocumentFiles.PrepareCopyFilesAsync)), "document.document", "document-one");
        var failure = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyAsync(new(DocumentFileOwnerPolicy.Key, "target-link"),
            "target-file", null, FileOwnerOperation.ConfirmCopy, source, default));
        Assert.Equal("Document.FileWriteContextInvalid", failure.Code);
    }

    [Fact]
    public void ReadFacts_PreserveTheValidatedReferenceChainWithoutFollowingCurrentPointersAgain()
    {
        var http = new HttpContextAccessor { HttpContext = Context() };
        var scope = new DocumentFileReadScope(http, null!);
        var identity = Identity();
        identity.Master["LatestVersionId"] = "another-version";
        identity.Version["CurrentIterationId"] = "another-iteration";
        using var phase = scope.Begin("document-one", DocumentFileReadPhase.PolicyDownload, identity);
        var facts = Assert.IsType<DocumentFileReadFacts>(scope.Resolve(scope.Target("document-one")).Facts);
        Assert.Equal("version-one", facts.VersionId);
        Assert.Equal("iteration-one", facts.IterationId);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("resource")]
    [InlineData("source-target")]
    [InlineData("stage")]
    [InlineData("caller")]
    [InlineData("member")]
    [InlineData("object")]
    public async Task Confirm_RejectsCrossedSourceBeforeInspectingCreationFacts(string problem)
    {
        var source = new DataExecutionWriteContext(new("document.document.internal.create", "standardCrud.create", "submit"), "document.document", null)
        { Internal = new("document.document.internal.create", "standardCrud.create", typeof(DocumentFiles), nameof(DocumentFiles.CreateWithFilesAsync)) };
        source = problem switch
        {
            "resource" => source with { ResourceKey = "part.part" },
            "source-target" => source with { ObjectId = "document-one" },
            "stage" => source with { Binding = source.Binding with { Stage = "prepare" } },
            "caller" => source with { Internal = source.Internal! with { Caller = typeof(DocumentFileExecutionScopeTests) } },
            "member" => source with { Internal = source.Internal! with { Member = nameof(DocumentFiles.ReadAsync) } },
            _ => source
        };
        FileOwnerOperationContext? context = problem == "missing" ? null : new FileOwnerCreationContext(source, problem == "object" ? "" : "document-one");
        var policy = new DocumentFileOwnerPolicy(null!, new DocumentFileCreationScope(null!), null!, null!, null!, null!, null!, new DocumentFileCopyScope(null!, new HttpContextAccessor()));
        var failure = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyAsync(new(DocumentFileOwnerPolicy.Key, "link-one"), "file-one", null,
            FileOwnerOperation.Confirm, context, default));
        Assert.Equal("Document.FileWriteContextInvalid", failure.Code);
    }

    [Fact]
    public async Task Release_ConsumesItsNewReadFactsAndRejectsThePreviousPhase()
    {
        var (access, _, scope, _, reads) = Create();
        DataExecutionTargetInput previous;
        using (scope.Begin("document-one", DocumentFileReadPhase.Metadata, Identity())) previous = scope.Target("document-one");
        using var phase = scope.Begin("document-one", DocumentFileReadPhase.PolicyRelease, Identity());
        var binding = new DataExecutionInternalBinding("document.files.release", "document.files.release", typeof(DocumentFileOwnerPolicy), nameof(DocumentFileOwnerPolicy.VerifyBatchAsync));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateInternalObjectAsync(binding, "target", previous));
        Assert.Equal(DataExecutionDecision.Allowed, (await access.EvaluateInternalObjectAsync(binding, "target", scope.Target("document-one"))).Decision);
        Assert.Equal(0, reads.Calls);
    }

    private static (DataExecutionAccess Access, HttpContextAccessor Http, DocumentFileReadScope Scope, CanonicalModulePlan Plan, Reads Reads) Create(bool wrongResourceOwner = false)
    {
        var compilation = new CanonicalModulePlanCompiler().Compile("document-file-scope",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray());
        Assert.True(compilation.IsSuccess, JsonSerializer.Serialize(compilation.Diagnostics));
        var http = new HttpContextAccessor { HttpContext = Context() };
        var scope = new DocumentFileReadScope(http, new ButtonExecutionAccess(compilation.Plan!, http, new ResourceCatalog(wrongResourceOwner), new ButtonChecks())); var reads = new Reads();
        return (new(compilation.Plan!, http, reads, new Decisions(), new ResourceCatalog(wrongResourceOwner), [new DocumentFileReadResolver(scope)]), http, scope, compilation.Plan!, reads);
    }

    private static DefaultHttpContext Context() => new() { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "scope-test")], "test")) };
    private static DocumentCurrentIdentity Identity() => new(new() { ["EID"] = "document-one" }, new() { ["EID"] = "version-one" }, new() { ["EID"] = "iteration-one" });
    private sealed class ResourceCatalog(bool wrongOwner) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("document.document", wrongOwner ? "Workspace" : "Document", "Document", "EID"), new("example.record", "Example", "ExampleRecord", "EID")];
    }
    private sealed class ButtonChecks : IObjectActionAccess
    {
        internal List<string> Calls { get; } = [];
        public Task<DataExecutionDecision> CheckAsync(string model, string id, IReadOnlyList<string> actions, CancellationToken token = default)
        { Calls.Add(model + ":" + string.Join(",", actions)); return Task.FromResult(DataExecutionDecision.Allowed); }
    }
    private sealed class Reads : IDataScopeObjectAccess
    {
        internal int Calls;
        public Task<bool> IsReadableAsync(string resource, string id, CancellationToken token = default) { Calls++; return Task.FromResult(true); }
    }
    private sealed class Decisions : IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsAllowedAsync(string resource, string id, string action, CancellationToken token = default) => Task.FromResult(true);
        public Task<bool> IsCandidateAllowedAsync(string resource, IReadOnlyDictionary<string, object?> candidate, string action, CancellationToken token = default) => throw new NotSupportedException();
    }
}
