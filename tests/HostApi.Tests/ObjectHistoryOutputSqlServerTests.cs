using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistoryOutputSqlServerTests
{
    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void HistoryOutputLocalization_ActualCandidateResolvesEverySafeOutputKeyInBothLanguages()
    {
        var root = Path.GetDirectoryName(typeof(ObjectHistoryOutputSqlServerTests).Assembly.Location)!;
        var manifest = new ICP.Platform.Kernel.PluginPackages.LocalizationRuntimeManifestBuilder().Build(root);
        var texts = ICP.Platform.Presentation.RuntimeTextCatalog.Parse(manifest);
        var services = new ServiceCollection();
        new ICP.Platform.Modules.Workspace.WorkspaceModule().ConfigureServices(services);
        new ICP.Platform.Modules.Part.PartModule().ConfigureServices(services);
        new ICP.Platform.Modules.Document.DocumentModule().ConfigureServices(services);
        new ICP.Platform.Modules.Authorization.AuthorizationModule().ConfigureServices(services);
        var policies = services.Where(service => service.ServiceType == typeof(ObjectHistoryEventPolicy))
            .Select(service => Assert.IsType<ObjectHistoryEventPolicy>(service.ImplementationInstance)).ToArray();
        Assert.NotEmpty(policies);
        var keys = new[] { "EventType", "OccurredAt", "ObjectType", "Actor", "Target", "Description", "IpAddress",
            "Unrecorded", "ActorUnavailable", "NameSource_historical", "NameSource_current", "NameSource_unrecorded", "NameSource_unavailable" };
        foreach (var locale in new[] { "zh-CN", "en-US" })
        {
            foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace(texts.GetText("ObjectHistory." + key, locale)));
            foreach (var key in policies.Select(policy => policy.DescriptionKey).Distinct(StringComparer.Ordinal)
                .Concat(["PartHistory.Title", "DocumentHistory.Title"]))
                Assert.False(string.IsNullOrWhiteSpace(texts.GetText(key, locale)));
        }
        Assert.Equal("操作", texts.GetText("ObjectHistory.EventType", "zh-CN"));
        Assert.Equal("Operation", texts.GetText("ObjectHistory.EventType", "en-US"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "选中及全部输出与页面同对象同筛选，XLSX/打印安全集合、伪造/撤权/故障与真实查询取消完整")]
    public async Task Outputs_UseTheSameSafeFilteredSet_RejectForgeryAndRevocation_AndPropagateCancellation()
    {
        var cancellation = new CancelHistoryQuery();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(cancellation)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "HistoryOutput296");
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "HistoryOutput296", "履历输出", "history-output296", "履历输出账号",
            Point(fixture, "workspace.spaces.access"), history);
        using var ordinary = actor.Client;
        var manage = Point(fixture, "part.part.authorize");
        var configuration = new DataScopeObjectDto("part.part", part.Id,
            [new("output-user", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
        foreach (var priority in new[] { 1, 2, 3 })
        {
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
                "/api/part/parts/" + part.Id + "/permissions", configuration with { Subjects = [configuration.Subjects[0] with { Priority = priority }] },
                OwnedResourceReadHttpRequests.ForExecution(fixture, manage, "part.part.authorization.manage", "authorization.object.manage", "part.part"));
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }
        var scopeJson = Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var basePath = "/api/workspace/content/" + part.LinkId + "/history";
        var queryScope = "?queryScope=" + scopeJson;
        var eventFilter = "&eventType=dataScope.object.changed";
        var page = await historyRequests.ReadAsync(ordinary, history, basePath + queryScope + eventFilter + "&pageSize=1");
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var request = new ObjectHistoryOutputRequest("all", new(PageSize: 1, EventType: "dataScope.object.changed"));
        using var printed = await historyRequests.SendAsync(ordinary, history, HttpMethod.Post, basePath + "/print" + queryScope, request);
        var all = (await ReadOkAsync(printed)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, all.Length);
        var ids = all.Select(item => item.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(ids.OrderDescending(StringComparer.Ordinal), ids); // 同stamp时仍按EID稳定倒序。
        Assert.Equal(page.GetProperty("items")[0].GetProperty("id").GetString(), ids[0]);
        Assert.All(all, item => Assert.Equal("dataScope.object.changed", item.GetProperty("eventType").GetString()));
        var selected = request with { Mode = "selected", SelectedIds = [ids[0], ids[2]] };
        using (var selectedPrint = await historyRequests.SendAsync(ordinary, history, HttpMethod.Post, basePath + "/print" + queryScope, selected))
            Assert.Equal(new[] { ids[0], ids[2] }, (await ReadOkAsync(selectedPrint)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        foreach (var (selection, rows) in new[] { (request, 4), (selected, 3) })
        {
            using var exported = await historyRequests.SendAsync(ordinary, history, HttpMethod.Post, basePath + "/export" + queryScope, selection);
            Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", exported.Content.Headers.ContentType?.MediaType);
            Assert.Contains("object-history.xlsx", exported.Content.Headers.ContentDisposition?.ToString());
            using var archive = new ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()));
            using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var xml = XDocument.Load(stream);
            Assert.Equal(rows, xml.Descendants().Count(element => element.Name.LocalName == "row"));
            var text = xml.ToString();
            Assert.Contains("修改对象直接授权", text, StringComparison.Ordinal);
            foreach (var hidden in new[] { "Configuration", "TraceId", "FieldsJson", part.Id, part.LinkId, actor.AccountId })
                Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        }
        // 描述匹配用户实际看见的安全译文，不匹配内部key或原ACL JSON。
        Assert.Equal(3, (await historyRequests.ReadAsync(ordinary, history, basePath + queryScope + "&description=" + Uri.EscapeDataString("修改对象直接授权"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await historyRequests.ReadAsync(ordinary, history, basePath + queryScope + "&description=DataScope.HistoryAuthorizationChanged")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await historyRequests.ReadAsync(ordinary, history, basePath + queryScope + "&description=Configuration")).GetProperty("total").GetInt32());
        foreach (var invalid in new[] { selected with { SelectedIds = [ids[0], "forged-record"] },
            selected with { Filter = selected.Filter with { EventType = "entity.ownership.initialized" } } })
        {
            using var forged = await historyRequests.SendAsync(ordinary, history, HttpMethod.Post, basePath + "/print" + queryScope, invalid);
            Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        }
        using (var empty = await historyRequests.SendAsync(ordinary, history, HttpMethod.Post, basePath + "/export" + queryScope, selected with { SelectedIds = [] }))
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        fixture.Fault.Mode = "query";
        try
        {
            using var failure = await historyRequests.SendAsync(ordinary, history, HttpMethod.Post, basePath + "/print" + queryScope, request);
            Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        }
        finally { fixture.Fault.Mode = ""; }
        using (var canceled = new CancellationTokenSource())
        {
            cancellation.Cancel = canceled;
            try
            {
                using var http = new HttpRequestMessage(HttpMethod.Post, basePath + "/export" + queryScope) { Content = JsonContent.Create(request) };
                http.Headers.Add("X-NewP-Feature", history.Position);
                http.Headers.Add("X-NewP-Button", fixture.Services.GetRequiredService<ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlan>().ActionDataAccessCatalog.Single(item => item.ModuleId == "Workspace" && item.ActionKey == "exportAll").Id);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ordinary.SendAsync(http, canceled.Token));
                Assert.True(cancellation.Fired);
            }
            finally { cancellation.Cancel = null; }
        }
        using (var denied = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, "/api/part/parts/" + part.Id + "/permissions",
            configuration with { Subjects = [configuration.Subjects[0] with { Actions = new Dictionary<string, string> { ["read"] = "deny" } }] },
            OwnedResourceReadHttpRequests.ForExecution(fixture, manage, "part.part.authorization.manage", "authorization.object.manage", "part.part")))
            Assert.Equal(HttpStatusCode.NoContent, denied.StatusCode);
        foreach (var output in new[] { "export", "print" })
        {
            using var revoked = await historyRequests.SendAsync(ordinary, history, HttpMethod.Post, basePath + "/" + output + queryScope, selected);
            Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        }
    }

    internal sealed class CancelHistoryQuery : DbCommandInterceptor
    {
        internal CancellationTokenSource? Cancel { get; set; }
        internal bool Fired { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Cancel is { } source && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("[BusinessChange]", StringComparison.Ordinal))
            {
                Fired = true;
                source.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return result;
        }
    }
}
