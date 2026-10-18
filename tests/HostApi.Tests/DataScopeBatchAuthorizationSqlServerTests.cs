using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeBatchAuthorizationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "overlapping mixed batches with opposite input order serialize full configurations and their exact audit records")]
    public async Task OverlappingBatches_WithReversedInputOrderCommitWholeConfigurationsAndHistoryInLockOrder()
    {
        var gate = new BatchLockGate();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(gate)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "BatchRacePart297");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "BatchRaceDoc297");
        var batch = fixture.FeatureCatalog.Single(point => point.Key == "batchAuthorize" && point.ActionScope == "list.pageActions");
        var firstActor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "BatchRaceA297", "First batch manager", "batchracea297", "First manager",
            Point(fixture, "workspace.spaces.access"), batch);
        var secondActor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "BatchRaceB297", "Second batch manager", "batchraceb297", "Second manager",
            Point(fixture, "workspace.spaces.access"), batch);
        using var first = firstActor.Client;
        using var second = secondActor.Client;
        foreach (var (target, resource) in new[] { (part, "part.part"), (document, "document.document") })
        {
            using var initialized = await FeatureHttpTestRequests.SendAsync(admin, Manage(fixture), HttpMethod.Put, Path(target.LinkId, position),
                new DataScopeObjectDto(resource, target.Id, [new(target.Id + "-race", "all", null, 0,
                    new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" })]));
            Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        }
        object Input(int priority, bool reverse) => new { mode = "U", sources = new[] { new { position,
            linkIds = reverse ? new[] { document.LinkId, part.LinkId } : new[] { part.LinkId, document.LinkId } } },
            resources = (reverse ? new[] { "document.document", "part.part" } : new[] { "part.part", "document.document" })
                .Select(resourceKey => new { resourceKey, subjects = new[] { new { type = "all", subjectId = (string?)null,
                    priority, actions = new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" } } } }).ToArray() };
        var registrations = new ConcurrentQueue<(string Actor, string Target)>();
        var context = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        fixture.Fault.AfterRegister = (change, _) =>
        {
            if (change.EventType == "dataScope.object.changed")
                registrations.Enqueue((context.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!, change.TargetId));
            return Task.CompletedTask;
        };
        gate.Context = context;
        gate.FirstActor = firstActor.AccountId;
        gate.SecondActor = secondActor.AccountId;
        gate.Active = true;
        var firstSave = FeatureHttpTestRequests.SendAsync(first, batch, HttpMethod.Post, "/api/workspace/content/permissions/batch", Input(11, false));
        Task<HttpResponseMessage>? secondSave = null;
        try
        {
            await gate.FirstGraphLocked.Task.WaitAsync(TimeSpan.FromSeconds(30));
            secondSave = FeatureHttpTestRequests.SendAsync(second, batch, HttpMethod.Post, "/api/workspace/content/permissions/batch", Input(22, true));
            await gate.SecondGraphAttempt.Task.WaitAsync(TimeSpan.FromSeconds(30));
            gate.Release.TrySetResult();
            using var firstResponse = await firstSave.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(2, (await ReadOkAsync(firstResponse)).GetProperty("changed").GetInt32());
            using var secondResponse = await secondSave.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(2, (await ReadOkAsync(secondResponse)).GetProperty("changed").GetInt32());
            var targets = new[] { "document.document/" + document.Id, "part.part/" + part.Id };
            Assert.Equal(new[] { firstActor.AccountId, firstActor.AccountId, secondActor.AccountId, secondActor.AccountId },
                registrations.Select(value => value.Actor).ToArray());
            Assert.Equal(targets.Concat(targets), registrations.Select(value => value.Target));
            var expectedLocks = targets.Select(target => "Authorization.DataScopeObject:" + target.Replace('/', ':')).ToArray();
            foreach (var actor in new[] { firstActor.AccountId, secondActor.AccountId })
                Assert.Equal(expectedLocks, gate.AclLocks.Where(value => value.Actor == actor).Select(value => value.Target));
            foreach (var target in new[] { part, document })
            {
                var stored = await FeatureHttpTestRequests.ReadAsync(admin, Manage(fixture), Path(target.LinkId, position));
                var subject = Assert.Single(stored.GetProperty("subjects").EnumerateArray());
                Assert.Equal(22, subject.GetProperty("priority").GetInt64());
                Assert.Equal("allow", subject.GetProperty("actions").GetProperty("authorize").GetString());
                Assert.Equal("allow", subject.GetProperty("actions").GetProperty("read").GetString());
            }
            using var scope = fixture.Services.CreateScope();
            var records = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
                .AsNoTracking().Where(row => EF.Property<string>(row, "EventType") == "dataScope.object.changed"
                    && (EF.Property<string>(row, "ActorId") == firstActor.AccountId || EF.Property<string>(row, "ActorId") == secondActor.AccountId)).ToArrayAsync();
            Assert.Equal(4, records.Length);
            foreach (var actor in new[] { firstActor.AccountId, secondActor.AccountId })
            {
                var own = records.Where(row => (string)row["ActorId"]! == actor).ToArray();
                Assert.Equal(targets, own.Select(row => (string)row["TargetId"]!).Order(StringComparer.Ordinal));
                Assert.Single(own.Select(row => row["OccurredAt"]).Distinct());
            }
        }
        finally
        {
            gate.Release.TrySetResult();
            var cancelPending = !firstSave.IsCompleted || secondSave is { IsCompleted: false };
            if (cancelPending) { first.CancelPendingRequests(); second.CancelPendingRequests(); }
            try
            {
                Task[] requests = secondSave is null ? [firstSave] : [firstSave, secondSave];
                await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (OperationCanceledException) when (cancelPending) { }
            finally { gate.Active = false; fixture.Fault.AfterRegister = null; }
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "one mixed batch shares its trusted stamp, emits exact per-EID ordinary history and semantic U repeats do not write")]
    public async Task MixedBatch_SharesOneStampAndPerObjectHistory_AndRepeatedUpdateIsNoOp()
    {
        var ticks = new AuditTicks();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.Replace(ServiceDescriptor.Scoped<IEntityAuditStampProvider>(provider => new AdvancingStamp(
                new HttpContextEntityAuditStampProvider(provider.GetRequiredService<IHttpContextAccessor>(), provider.GetRequiredService<TimeProvider>()), ticks))),
            includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "BatchStampPart297");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "BatchStampDoc297");
        var batch = fixture.FeatureCatalog.Single(point => point.Key == "batchAuthorize" && point.ActionScope == "list.pageActions");
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "BatchStamp297", "Batch stamp manager", "batchstamp297", "Batch stamp manager",
            Point(fixture, "workspace.spaces.access"), batch, history);
        using var ordinary = actor.Client;
        var input = new { mode = "U", sources = new[] { new { position, linkIds = new[] { document.LinkId, part.LinkId } } },
            resources = new[] { "part.part", "document.document" }.Select(resourceKey => new { resourceKey, subjects = new[] {
                new { type = "all", subjectId = (string?)null, priority = 4, actions = new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" } } } }).ToArray() };
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, "/api/workspace/content/permissions/batch", input))
            Assert.Equal(2, (await ReadOkAsync(response)).GetProperty("changed").GetInt32());
        var recordIds = new Dictionary<string, string>();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var targets = new[] { "part.part/" + part.Id, "document.document/" + document.Id };
            var events = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().Where(row =>
                EF.Property<string>(row, "EventType") == "dataScope.object.changed" && targets.Contains(EF.Property<string>(row, "TargetId"))).ToArrayAsync();
            Assert.Equal(2, events.Length);
            Assert.Equal(2, events.Select(row => row["TargetId"]).Distinct().Count());
            var stamp = Assert.Single(events.Select(row => row["OccurredAt"]).Distinct());
            Assert.All(events, row => { Assert.Equal(actor.AccountId, row["ActorId"]); recordIds.Add((string)row["TargetId"]!, (string)row["EID"]!); });
            var subjects = await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
                .Where(row => EF.Property<string>(row, "ObjectId") == part.Id || EF.Property<string>(row, "ObjectId") == document.Id).ToArrayAsync();
            Assert.Equal(2, subjects.Length);
            var ids = subjects.Select(row => (string)row["EID"]!).ToArray();
            var actions = await db.Set<Dictionary<string, object?>>("DataScopeObjectAction").AsNoTracking()
                .Where(row => ids.Contains(EF.Property<string>(row, "EntryId"))).ToArrayAsync();
            Assert.Equal(4, actions.Length);
            Assert.All(subjects.Concat(actions), row => { Assert.Equal(stamp, row["CreatedAt"]); Assert.Equal(actor.AccountId, row["CreatedBy"]); });
        }
        var before = await SnapshotAsync(fixture, part.Id, document.Id);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post, "/api/workspace/content/permissions/batch", input))
            Assert.Equal(0, (await ReadOkAsync(response)).GetProperty("changed").GetInt32());
        Assert.Equal(before, await SnapshotAsync(fixture, part.Id, document.Id));
        foreach (var (target, resourceKey) in new[] { (part, "part.part"), (document, "document.document") })
        {
            var path = "/api/workspace/content/" + target.LinkId + "/history?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web))) + "&eventType=dataScope.object.changed";
            var page = await FeatureHttpTestRequests.ReadAsync(ordinary, history, path);
            Assert.Equal(1, page.GetProperty("total").GetInt32());
            var item = Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(recordIds[resourceKey + "/" + target.Id], item.GetProperty("id").GetString());
            var text = page.GetRawText();
            Assert.DoesNotContain(part.Id, text);
            Assert.DoesNotContain(document.Id, text);
            Assert.DoesNotContain("subjects", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TraceId", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "explicit current batch reads are qualification checked, read-only and never infer an earlier commit")]
    public async Task CurrentConfiguration_ReadsTheWholeQualifiedSetWithoutWrites_AndRevocationRejectsTheFinalBatch()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "BatchCurrentPart297");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "BatchCurrentDoc297");
        var manage = Manage(fixture);
        var batch = fixture.FeatureCatalog.Single(point => point.Key == "batchAuthorize" && point.ActionScope == "list.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Current297", "Current batch manager", "current297", "Current manager",
            Point(fixture, "workspace.spaces.access"), batch);
        using var ordinary = actor.Client;
        foreach (var (target, resource) in new[] { (part, "part.part"), (document, "document.document") })
        {
            using var initialized = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, Path(target.LinkId, position),
                new DataScopeObjectDto(resource, target.Id, [new(target.Id + "-current", "all", null, 1,
                    new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" }, ExpiresAt: "2099-01-01T00:00:00Z")]));
            Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        }
        var input = new { sources = new[] { new { position, linkIds = new[] { document.LinkId, part.LinkId, part.LinkId } } } };
        var before = await SnapshotAsync(fixture, part.Id, document.Id);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
            "/api/workspace/content/permissions/batch/current", input))
        {
            var current = await ReadOkAsync(response);
            Assert.Equal(2, current.GetArrayLength());
            Assert.Equal(new[] { "document.document", "part.part" }, current.EnumerateArray().Select(item => item.GetProperty("resourceKey").GetString()).ToArray());
            foreach (var configuration in current.EnumerateArray())
            {
                var expected = configuration.GetProperty("resourceKey").GetString() == "part.part" ? part : document;
                Assert.Equal(expected.Id, configuration.GetProperty("objectId").GetString());
                var subject = Assert.Single(configuration.GetProperty("subjects").EnumerateArray());
                Assert.Equal(expected.Id + "-current", subject.GetProperty("id").GetString());
                Assert.Equal("2099-01-01T00:00:00Z", subject.GetProperty("expiresAt").GetString());
                Assert.Equal("allow", subject.GetProperty("actions").GetProperty("authorize").GetString());
            }
        }
        Assert.Equal(before, await SnapshotAsync(fixture, part.Id, document.Id));
        using (var revoked = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, Path(part.LinkId, position),
            new DataScopeObjectDto("part.part", part.Id, [new(part.Id + "-deny-current", "user", actor.AccountId, 1,
                new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "deny" })])))
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        before = await SnapshotAsync(fixture, part.Id, document.Id);
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
            "/api/workspace/content/permissions/batch/current", input))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var text = await denied.Content.ReadAsStringAsync();
            Assert.DoesNotContain(part.Id, text);
            Assert.DoesNotContain(document.Id, text);
        }
        using (var rejected = await FeatureHttpTestRequests.SendAsync(ordinary, batch, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", new { input.sources, mode = "R", resources = new[] { "part.part", "document.document" }
                .Select(resourceKey => new { resourceKey, subjects = new[] { new { type = "all", subjectId = (string?)null,
                    priority = 1, actions = new Dictionary<string, string> { ["read"] = "allow" } } } }).ToArray() }))
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(before, await SnapshotAsync(fixture, part.Id, document.Id));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "mixed batch replaces complete selectors, deduplicates EIDs and rolls back invalid resource actions")]
    public async Task MixedTargets_UpdateCompleteSelectorsAndRejectInvalidActionsAtomically()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "BatchPart297");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "BatchDoc297");
        var folder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "BatchOther297");
        var other = position with { LinkIds = [.. position.LinkIds, folder.LinkId] };
        using (var added = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position = other, target = new { modelKey = "Part", id = part.Id } }))
            await ReadOkAsync(added);
        string secondLink;
        using (var scope = fixture.Services.CreateScope())
            secondLink = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .Where(row => EF.Property<string>(row, "PEID") == folder.Id && EF.Property<string>(row, "CEID") == part.Id)
                .Select(row => EF.Property<string>(row, "LID")).SingleAsync();
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "workspace.folder.authorize"
            && point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        foreach (var (target, resource) in new[] { (part, "part.part"), (document, "document.document") })
        {
            var input = new DataScopeObjectDto(resource, target.Id,
                [new(target.Id + "-all", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" }),
                 new(target.Id + "-creator", "creator", null, 2, new Dictionary<string, string> { ["read"] = "allow" })]);
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, Path(target.LinkId, position), input);
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        }
        var beforeEvents = await EventsAsync(fixture, part.Id, document.Id);
        var sources = new[] { new { position, linkIds = new[] { part.LinkId, document.LinkId } },
            new { position = other, linkIds = new[] { secondLink } } };
        object Group(string resource, string action = "read") => new { resourceKey = resource, subjects = new[] {
            new { type = "all", subjectId = (string?)null, priority = 8, actions = new Dictionary<string, string> {
                [action] = "deny", ["edit"] = "unset" }, matchOptions = (object?)null, expiresAt = "2099-01-01T00:00:00Z" } } };
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", new { sources, mode = "U", resources = new[] { Group("part.part"), Group("document.document") } }))
        {
            Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            var receipt = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.Equal(2, receipt.GetProperty("targets").GetInt32());
            Assert.Equal(2, receipt.GetProperty("changed").GetInt32());
        }
        foreach (var target in new[] { part, document })
        {
            var config = await FeatureHttpTestRequests.ReadAsync(admin, manage, Path(target.LinkId, position));
            var subjects = config.GetProperty("subjects").EnumerateArray().ToArray();
            Assert.Equal(2, subjects.Length);
            var selected = subjects.Single(subject => subject.GetProperty("type").GetString() == "all");
            Assert.Equal(8, selected.GetProperty("priority").GetInt64());
            Assert.Equal("2099-01-01T00:00:00Z", selected.GetProperty("expiresAt").GetString());
            Assert.Equal("deny", selected.GetProperty("actions").GetProperty("read").GetString());
            Assert.False(selected.GetProperty("actions").TryGetProperty("edit", out _));
            var retained = subjects.Single(subject => subject.GetProperty("type").GetString() == "creator");
            Assert.Equal(target.Id + "-creator", retained.GetProperty("id").GetString());
            Assert.Equal("allow", retained.GetProperty("actions").GetProperty("read").GetString());
        }
        Assert.Equal(beforeEvents + 2, await EventsAsync(fixture, part.Id, document.Id));
        var previousPart = await FeatureHttpTestRequests.ReadTextAsync(admin, manage, Path(part.LinkId, position));
        var previousDocument = await FeatureHttpTestRequests.ReadTextAsync(admin, manage, Path(document.LinkId, position));
        using (var rejected = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", new { sources, mode = "R", resources = new[] {
                Group("part.part", "download"), Group("document.document", "download") } }))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(previousPart, await FeatureHttpTestRequests.ReadTextAsync(admin, manage, Path(part.LinkId, position)));
        Assert.Equal(previousDocument, await FeatureHttpTestRequests.ReadTextAsync(admin, manage, Path(document.LinkId, position)));
        Assert.Equal(beforeEvents + 2, await EventsAsync(fixture, part.Id, document.Id));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "R replaces every selector, clearing requires explicit confirmation and committed self-revocation is success")]
    public async Task ReplaceMode_ConfirmsClearAndCommitsSelfRevocationWithoutReadingBack()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "BatchReplacePart297");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "BatchReplaceDoc297");
        var manage = Manage(fixture);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Batch297", "Batch manager", "batch297", "Batch manager",
            Point(fixture, "workspace.spaces.access"), manage);
        using var ordinary = actor.Client;
        foreach (var (target, resource) in new[] { (part, "part.part"), (document, "document.document") })
        {
            using var initialized = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, Path(target.LinkId, position),
                new DataScopeObjectDto(resource, target.Id, [new(target.Id + "-old", "creator", null, 1,
                    new Dictionary<string, string> { ["read"] = "allow" })]));
            Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        }
        object Resource(string key, bool clear = false) => new { resourceKey = key, subjects = clear ? Array.Empty<object>() : new object[] {
            new { type = "user", subjectId = actor.AccountId, priority = 10,
                actions = new Dictionary<string, string> { ["read"] = "deny", ["authorize"] = "deny", ["edit"] = "allow" } } } };
        object Input(bool clear = false, bool confirm = false) => new { mode = "R", confirmClear = confirm,
            sources = new[] { new { position, linkIds = new[] { part.LinkId, document.LinkId } } },
            resources = new[] { Resource("part.part", clear), Resource("document.document", clear) } };
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", Input()))
        {
            var receipt = await ReadOkAsync(saved);
            Assert.Equal(2, receipt.GetProperty("changed").GetInt32());
            Assert.False(receipt.TryGetProperty("subjects", out _));
        }
        foreach (var target in new[] { part, document })
        {
            using var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Get, Path(target.LinkId, position));
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            var stored = await FeatureHttpTestRequests.ReadAsync(admin, manage, Path(target.LinkId, position));
            var subject = Assert.Single(stored.GetProperty("subjects").EnumerateArray());
            Assert.Equal("user", subject.GetProperty("type").GetString());
            Assert.Equal(actor.AccountId, subject.GetProperty("subjectId").GetString());
        }
        var before = await SnapshotAsync(fixture, part.Id, document.Id);
        using (var unconfirmed = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", Input(clear: true)))
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal(before, await SnapshotAsync(fixture, part.Id, document.Id));
        using (var cleared = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", Input(clear: true, confirm: true)))
            Assert.Equal(2, (await ReadOkAsync(cleared)).GetProperty("changed").GetInt32());
        before = await SnapshotAsync(fixture, part.Id, document.Id);
        using (var unchanged = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", Input(clear: true, confirm: true)))
            Assert.Equal(0, (await ReadOkAsync(unchanged)).GetProperty("changed").GetInt32());
        Assert.Equal(before, await SnapshotAsync(fixture, part.Id, document.Id));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "an audit failure or real cancellation after SQL flush rolls back every target and success record")]
    public async Task BatchFailureAndRequestCancellation_AfterFlushRollBackEveryTargetAndAudit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "BatchFaultPart297");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "BatchFaultDoc297");
        var manage = Manage(fixture);
        var input = new { mode = "R", sources = new[] { new { position, linkIds = new[] { part.LinkId, document.LinkId } } },
            resources = new[] { "part.part", "document.document" }.Select(resource => new { resourceKey = resource, subjects = new[] {
                new { type = "all", subjectId = (string?)null, priority = 5, actions = new Dictionary<string, string> { ["read"] = "allow" } } } }).ToArray() };
        var before = await SnapshotAsync(fixture, part.Id, document.Id);
        foreach (var cancel in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var seen = 0;
            var flushed = false;
            fixture.Fault.AfterRegister = async (change, token) =>
            {
                if (change.EventType != "dataScope.object.changed") return;
                seen++;
                var context = fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                await context.RequestServices.GetRequiredService<IPlatformTransactionExecutor>().FlushAsync(token);
                flushed = true;
                if (seen != 2) return;
                if (!cancel) throw new InvalidOperationException("injected second batch audit failure");
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            try
            {
                if (!cancel)
                {
                    using var failed = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Post,
                        "/api/workspace/content/permissions/batch", input);
                    Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                }
                else
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/permissions/batch");
                    request.Headers.Add("X-NewP-Feature", manage.Position);
                    request.Content = JsonContent.Create(input, options: new JsonSerializerOptions());
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    {
                        using var response = await admin.SendAsync(request, cancellation.Token);
                        Assert.Fail($"Expected cancellation, HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    });
                }
            }
            finally { fixture.Fault.AfterRegister = null; }
            Assert.True(flushed);
            Assert.Equal(2, seen);
            Assert.Equal(before, await SnapshotAsync(fixture, part.Id, document.Id));
        }
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
            "/api/workspace/content/references/" + document.LinkId, new { position })) await ReadOkAsync(removed);
        using var stale = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Post,
            "/api/workspace/content/permissions/batch", input);
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        Assert.Equal(before, await SnapshotAsync(fixture, part.Id, document.Id));
    }

    private static FeaturePoint Manage(BusinessAuditHostFixture fixture) => fixture.FeatureCatalog.First(point =>
        point.BehaviorKey == "workspace.folder.authorize" && point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");

    private static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture, string part, string document)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var subjects = await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
            .Where(row => EF.Property<string>(row, "ObjectId") == part || EF.Property<string>(row, "ObjectId") == document)
            .OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
        var entryIds = subjects.Select(row => (string)row["EID"]!).ToArray();
        var actions = await db.Set<Dictionary<string, object?>>("DataScopeObjectAction").AsNoTracking()
            .Where(row => entryIds.Contains(EF.Property<string>(row, "EntryId"))).OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
        var events = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "EventType") == "dataScope.object.changed"
                && (EF.Property<string>(row, "TargetId") == "part.part/" + part || EF.Property<string>(row, "TargetId") == "document.document/" + document))
            .OrderBy(row => EF.Property<string>(row, "EID")).ToArrayAsync();
        return JsonSerializer.Serialize(new { subjects, actions, events });
    }

    private static string Path(string link, Position position) => "/api/workspace/content/" + link + "/permissions?queryScope="
        + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static async Task<int> EventsAsync(BusinessAuditHostFixture fixture, string part, string document)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
            .CountAsync(row => EF.Property<string>(row, "EventType") == "dataScope.object.changed"
                && (EF.Property<string>(row, "TargetId") == "part.part/" + part || EF.Property<string>(row, "TargetId") == "document.document/" + document));
    }
    // 调度两个真实HTTP请求的现图锁；同时记录实际ACL取得顺序，不改判权、事务或锁实现。
    private sealed class BatchLockGate : DbCommandInterceptor
    {
        internal IHttpContextAccessor? Context { get; set; }
        internal string FirstActor { get; set; } = "";
        internal string SecondActor { get; set; } = "";
        internal bool Active { get; set; }
        internal TaskCompletionSource FirstGraphLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SecondGraphAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<(string Actor, string Target)> AclLocks { get; } = new();

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Active && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)
                && Context?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) == SecondActor)
                SecondGraphAttempt.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken token = default)
        {
            if (!Active || !command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)) return result;
            var actor = Context?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal) && actor == FirstActor)
            {
                FirstGraphLocked.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            var target = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<string>()
                .FirstOrDefault(value => value.StartsWith("Authorization.DataScopeObject:", StringComparison.Ordinal));
            if (target is not null) AclLocks.Enqueue((actor, target));
            return result;
        }
    }

    private sealed class AuditTicks { internal long Count; }

    // 真实actor保持不变，每次底层取stamp推进1ms，不能靠固定时钟掩盖逐目标另取stamp。
    private sealed class AdvancingStamp(HttpContextEntityAuditStampProvider inner, AuditTicks ticks) : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent()
        {
            var stamp = inner.GetCurrent();
            return new(stamp.ActorId, stamp.OccurredAtUtc.AddMilliseconds(Interlocked.Increment(ref ticks.Count)));
        }
    }

}
