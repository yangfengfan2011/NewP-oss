using ICP.Platform.Modules.Files.Contracts;
using System.Security.Claims;
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
    [Theory]
    [InlineData("valid")]
    [InlineData("duplicate-same-behavior")]
    [InlineData("behavior")]
    [InlineData("two-behaviors")]
    [InlineData("feature")]
    [InlineData("execution")]
    [InlineData("resource")]
    [InlineData("owner")]
    [InlineData("resource-owner")]
    public async Task OwnEndpoint_UsesItsRealStaticFeatureAndRejectsCrossedSources(string problem)
    {
        var (access, http, scope, plan, reads) = Create(problem == "resource-owner");
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.files.download");
        var metadata = new List<object> { new EndpointModuleAttribute(problem == "owner" ? "Workspace" : "Document"),
            new RequireFeatureAttribute(problem == "behavior" ? "document.document.read" : "document.document.download") };
        if (problem == "duplicate-same-behavior") metadata.Add(new RequireFeatureAttribute("document.document.download"));
        if (problem == "two-behaviors") metadata.Add(new RequireFeatureAttribute("document.document.read"));
        http.HttpContext!.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "owned-document-download"));
        http.HttpContext.Request.Headers["X-NewP-Data-Declaration"] = problem == "execution"
            ? Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.read").Id : declaration.Id;
        http.HttpContext.Request.Headers["X-NewP-Feature"] = problem == "feature" ? "wrong-feature" : Assert.Single(declaration.FeaturePositions);
        using var phase = scope.Begin("document-one", DocumentFileReadPhase.Open);
        var target = problem == "resource" ? scope.Target("document-one") with { ResourceKey = "example.record" } : scope.Target("document-one");
        var action = async () =>
        {
            var result = new List<DataExecutionTargetDecision>();
            await foreach (var decision in access.EvaluateObjectsAsync(new("document.files.download", "document.files.download", "read"), "target", [target])) result.Add(decision);
            return Assert.Single(result).Decision;
        };
        if (problem is "valid" or "duplicate-same-behavior") Assert.Equal(DataExecutionDecision.Allowed, await action());
        else await Assert.ThrowsAsync<ActionDataAccessException>(action);
        Assert.Equal(0, reads.Calls);
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
    public async Task InternalFeedback_AcceptsOnlyItsFixedCallerAndCurrentReadFacts(string problem)
    {
        var (access, http, scope, _, reads) = Create();
        using var phase = scope.Begin("document-one", DocumentFileReadPhase.Metadata, Identity());
        var target = scope.Target("document-one");
        var binding = new DataExecutionInternalBinding("document.files.download.feedback", "document.files.download", typeof(DocumentFiles), nameof(DocumentFiles.ReadAsync));
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

    [Fact]
    public async Task GenericProvider_StillRequiresItsOwnEndpointOwnerAndResourceFeature()
    {
        var (access, http, _, plan, _) = Create();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "example.record.authorization.manage");
        http.HttpContext!.Request.Headers["X-NewP-Data-Declaration"] = declaration.Id;
        http.HttpContext.Request.Headers["X-NewP-Feature"] = Assert.Single(declaration.FeaturePositions);
        var binding = new DataExecutionBindingReference(declaration.Execution, declaration.Provider!, "prepare");
        http.HttpContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute("Authorization"), new ResourceFeatureBindingAttribute("authorize")), "generic"));
        Assert.Equal(DataExecutionDecision.Allowed, await access.EvaluateObjectAsync(binding, "example.record", "target", "example-one"));
        http.HttpContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute("Example"), new RequireFeatureAttribute("example.record.authorize")), "wrong-owned-endpoint"));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateObjectAsync(binding, "example.record", "target", "example-one"));
    }

    [Fact]
    public async Task LegacyCopyRead_RejectsAnArbitraryImplementationIdentityBeforeAnyQuery()
    {
        // 未构造后续基础设施，证明明确caller守卫在任何查询之前拒绝。
        var policy = new DocumentFileOwnerPolicy(null!, null!, null!, null!, Microsoft.Extensions.Options.Options.Create(new DocumentOptions()), null!, null!);
        var source = new ICP.Platform.Modules.Files.Contracts.FileOwnerLegacyCopyReadContext(typeof(ICP.Platform.Modules.Files.Contracts.IFileCopyService), "PrepareCopyAsync");
        var failure = await Assert.ThrowsAsync<ICP.Platform.Modules.Files.Contracts.FileLifecycleException>(() => policy.VerifyBatchAsync([], ICP.Platform.Modules.Files.Contracts.FileOwnerOperation.CopySource, source, default));
        Assert.Equal("Document.FileReadContextInvalid", failure.Code);
    }

    [Theory]
    [InlineData("PrepareCopyAsync", true)]
    [InlineData("ConfirmCopyAsync", true)]
    [InlineData("CancelCopyAsync", false)]
    [InlineData("ReadCopyAsync", false)]
    public async Task LegacyCopySource_AcceptsOnlyItsTwoActualSourceReadingMembers(string member, bool allowed)
    {
        var policy = new DocumentFileOwnerPolicy(null!, null!, null!, null!, null!, null!, null!);
        var source = new FileOwnerLegacyCopyReadContext(typeof(ICP.Platform.Modules.Files.Services.FileLifecycleService), member);
        if (allowed) await policy.VerifyBatchAsync([], FileOwnerOperation.CopySource, source, default);
        else
        {
            var failure = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyBatchAsync([], FileOwnerOperation.CopySource, source, default));
            Assert.Equal("Document.FileReadContextInvalid", failure.Code);
        }
    }

    [Fact]
    public async Task LegacyCopyConfirmation_DoesNotBorrowThePrepareSourceIdentity()
    {
        var policy = new DocumentFileOwnerPolicy(null!, null!, null!, null!, null!, null!, null!);
        var source = new FileOwnerLegacyCopyReadContext(typeof(ICP.Platform.Modules.Files.Services.FileLifecycleService), "PrepareCopyAsync");
        var failure = await Assert.ThrowsAsync<FileLifecycleException>(() => policy.VerifyAsync(new(DocumentFileOwnerPolicy.Key, "target-link"),
            "target-file", null, FileOwnerOperation.ConfirmCopy, source, default));
        Assert.Equal("Document.FileReadContextInvalid", failure.Code);
    }

    [Fact]
    public void ReadFacts_PreserveTheValidatedReferenceChainWithoutFollowingCurrentPointersAgain()
    {
        var http = new HttpContextAccessor { HttpContext = Context() };
        var scope = new DocumentFileReadScope(http);
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
        var policy = new DocumentFileOwnerPolicy(null!, null!, null!, null!, null!, null!, null!);
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
        var scope = new DocumentFileReadScope(http); var reads = new Reads();
        return (new(compilation.Plan!, http, reads, new Decisions(), new ResourceCatalog(wrongResourceOwner), [new DocumentFileReadResolver(scope)]), http, scope, compilation.Plan!, reads);
    }

    private static DefaultHttpContext Context() => new() { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "scope-test")], "test")) };
    private static DocumentCurrentIdentity Identity() => new(new() { ["EID"] = "document-one" }, new() { ["EID"] = "version-one" }, new() { ["EID"] = "iteration-one" });
    private sealed class ResourceCatalog(bool wrongOwner) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("document.document", wrongOwner ? "Workspace" : "Document", "Document", "EID"), new("example.record", "Example", "ExampleRecord", "EID")];
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
