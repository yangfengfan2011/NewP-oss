using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PartSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实零组件创建、独立候选及只读属性")]
    public async Task Create_ReadsIndependentPartAndCanonicalAuditDisplay()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/part/parts")
        {
            Content = JsonContent.Create(new { ECODE = "PART-001", ENAME = "首个零组件", Description = new string('字', 500) }, options: new JsonSerializerOptions())
        };
        request.Headers.Add("X-NewP-Feature", Create(fixture).Position);
        request.Headers.Add("X-NewP-Data-Declaration", CreateDeclaration(fixture).Id);
        using var response = await admin.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = result.GetProperty("fields");
        Assert.Equal(500, fields.GetProperty("Description").GetString()!.Length);
        Assert.StartsWith("Part_", result.GetProperty("fields").GetProperty("EID").GetString());
        Assert.Equal(fixture.ActorId, fields.GetProperty("CreatedBy").GetString());
        Assert.Equal(fields.GetProperty("CreatedBy").GetString(), fields.GetProperty("UpdatedBy").GetString());
        Assert.Equal(fields.GetProperty("CreatedAt").GetString(), fields.GetProperty("UpdatedAt").GetString());
        Assert.Equal("审计管理员", fields.GetProperty("UpdatedBy_DisplayValue").GetString());
        Assert.EndsWith("Z", fields.GetProperty("UpdatedAt").GetString());
        Assert.False(string.IsNullOrWhiteSpace(fields.GetProperty("UpdatedAt_DisplayValue").GetString()));
        var read = Entry(fixture);
        var id = result.GetProperty("fields").GetProperty("EID").GetString();
        var detail = await OwnedResourceReadHttpRequests.ReadAsync(fixture, admin, read, "part.part", "read", "/api/part/parts/" + id);
        Assert.Equal(500, detail.GetProperty("fields").GetProperty("Description").GetString()!.Length);
        var candidates = await OwnedResourceReadHttpRequests.ReadAsync(fixture, admin, read, "part.part", "list", "/api/part/parts?keyword=PART-001&fields=EID,ECODE,ENAME&pageSize=1");
        var candidate = Assert.Single(candidates.GetProperty("items").EnumerateArray());
        Assert.Equal(id, candidate.GetProperty("EID").GetString());
        Assert.Equal(new[] { "EID", "ECODE", "ENAME" }, candidate.EnumerateObject().Select(p => p.Name));
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool>());
        using var relogged = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(relogged);
        using var login = await relogged.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "Audit-test-secure-938!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(relogged);
        var restored = await OwnedResourceReadHttpRequests.ReadAsync(fixture, relogged, Entry(fixture), "part.part", "read", "/api/part/parts/" + id);
        // 重启核对全部字段值，JSON 对象的成员顺序不参与详情契约。
        Assert.Equal(fields.EnumerateObject().OrderBy(property => property.Name).Select(property => (property.Name, property.Value.GetRawText())),
            restored.GetProperty("fields").EnumerateObject().OrderBy(property => property.Name).Select(property => (property.Name, property.Value.GetRawText())));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "零组件字段边界与并发唯一编码使用真实SQL和标准写入")]
    public async Task Create_ProtectsServerFieldsAndRejectsInvalidValuesAndConcurrentCodes()
    {
        var scans = new PartScanObserver();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(scans)), includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var create = Create(fixture);
        foreach (var (values, field, code) in new (object, string, string)[]
        {
            (new { ENAME = "缺编码" }, "ECODE", "Part.CodeRequired"),
            (new { ECODE = "  ", ENAME = "空编码" }, "ECODE", "Part.CodeRequired"),
            (new { ECODE = "NO-NAME" }, "ENAME", "Part.NameRequired"),
            (new { ECODE = "EMPTY-NAME", ENAME = " \t " }, "ENAME", "Part.NameRequired"),
            (new { ECODE = "TOO-LONG", ENAME = "超长", Description = new string('字', 501) }, "Description", "Part.DescriptionTooLong"),
            (new { ECODE = "AUDIT", ENAME = "审计伪造", CreatedBy = "other" }, "CreatedBy", "StandardCrud.ServerOwnedFieldReadOnly"),
            (new { ECODE = "ID", ENAME = "主键伪造", EID = "chosen-id" }, "EID", "StandardCrud.ServerOwnedFieldReadOnly")
        })
        {
            using var denied = await SendCreateAsync(fixture, admin, values);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            var error = await denied.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(code, error.GetProperty("code").GetString());
            Assert.Equal(field, Assert.Single(error.GetProperty("fieldErrors").EnumerateArray()).GetProperty("field").GetString());
        }
        Assert.Equal(0, (await OwnedResourceReadHttpRequests.ReadAsync(fixture, admin, Entry(fixture), "part.part", "list", "/api/part/parts")).GetProperty("total").GetInt32());
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendCreateAsync(fixture, admin, new { ECODE = "RACE", ENAME = "同名允许" })));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
                Assert.True(code is "Part.CodeDuplicated" or "StandardCrud.PersistenceConflict", code);
            }
            response.Dispose();
        }
        var second = await CreatePartAsync(fixture, admin, new { ECODE = "OTHER", ENAME = "同名允许", Description = (string?)null });
        Assert.Equal(JsonValueKind.Null, second.GetProperty("fields").GetProperty("Description").ValueKind);
        using var duplicate = await SendCreateAsync(fixture, admin, new { ECODE = "RACE", ENAME = "相同编码" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("Part.CodeDuplicated", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            using var forbidden = await FeatureHttpTestRequests.SendAsync(admin, create, method, "/api/part/parts/" + second.GetProperty("fields").GetProperty("EID").GetString(), new { ENAME = "旁路改名" });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using var options = await FeatureHttpTestRequests.SendAsync(admin, Entry(fixture), HttpMethod.Get, "/api/part/parts/options");
        Assert.Equal(HttpStatusCode.Forbidden, options.StatusCode);
        Assert.Equal(2, (await OwnedResourceReadHttpRequests.ReadAsync(fixture, admin, Entry(fixture), "part.part", "list", "/api/part/parts")).GetProperty("total").GetInt32());
        Assert.Empty(scans.UnboundedReads);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "独立候选交集正式read权限、真实SQL分页与写后不可读成功回执")]
    public async Task Candidates_ApplyResourceReadPlanBeforeCountingAndCreatingUnreadablePartReturnsReceipt()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Entry(fixture); var create = Create(fixture);
        for (var i = 0; i < 7; i++) await CreatePartAsync(fixture, admin, new { ECODE = "CAND-" + i, ENAME = "独立候选" });
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartReader", "零组件读取", "part-reader", "零组件用户", entry);
        using var ordinary = actor.Client;
        using var noCreate = await SendCreateAsync(fixture, ordinary, new { ECODE = "DENY", ENAME = "拒绝" });
        Assert.Equal(HttpStatusCode.Forbidden, noCreate.StatusCode);
        await RuleAsync(fixture, admin, "HidePart", "ECODE", "CAND-2", "read");
        var first = await OwnedResourceReadHttpRequests.ReadAsync(fixture, ordinary, entry, "part.part", "list", "/api/part/parts?keyword=CAND&pageSize=2&fields=EID,ECODE,ENAME");
        Assert.Equal(6, first.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "CAND-0", "CAND-1" }, first.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("ECODE").GetString()));
        var page = await OwnedResourceReadHttpRequests.ReadAsync(fixture, ordinary, entry, "part.part", "list", "/api/part/parts?keyword=独立候选&pageSize=2&pageIndex=2&fields=EID,ECODE,ENAME");
        Assert.Equal(new[] { "CAND-3", "CAND-4" }, page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("ECODE").GetString()));
        var hiddenId = Assert.Single((await OwnedResourceReadHttpRequests.ReadAsync(fixture, admin, entry, "part.part", "list", "/api/part/parts?keyword=CAND-2&fields=EID")).GetProperty("items").EnumerateArray()).GetProperty("EID").GetString();
        using var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, "/api/part/parts/" + hiddenId,
            dataAccess: OwnedResourceReadHttpRequests.Declaration(fixture, entry, "part.part", "read"));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        await new FeatureHttpTestRequests(fixture).GrantAsync(admin, actor.RoleId, entry, create);
        await RuleAsync(fixture, admin, "HideCreated", "ECODE", "HIDDEN-NEW", "read");
        var receipt = await CreatePartAsync(fixture, ordinary, new { ECODE = "HIDDEN-NEW", ENAME = "创建后不可读" });
        Assert.Single(receipt.EnumerateObject()); Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, (await OwnedResourceReadHttpRequests.ReadAsync(fixture, ordinary, entry, "part.part", "list", "/api/part/parts?keyword=HIDDEN-NEW")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await OwnedResourceReadHttpRequests.ReadAsync(fixture, admin, entry, "part.part", "list", "/api/part/parts?keyword=HIDDEN-NEW")).GetProperty("total").GetInt32());
        await RuleAsync(fixture, admin, "DenyCreate", "ECODE", "DENY-NEW", "create");
        using var deniedCreate = await SendCreateAsync(fixture, ordinary, new { ECODE = "DENY-NEW", ENAME = "数据动作拒绝" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
        await new FeatureHttpTestRequests(fixture).GrantAsync(admin, actor.RoleId);
        using var noEntry = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, "/api/part/parts",
            dataAccess: OwnedResourceReadHttpRequests.Declaration(fixture, entry, "part.part", "list"));
        Assert.Equal(HttpStatusCode.Forbidden, noEntry.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "零组件已提交后SQL展示故障不反转成功或重放写入")]
    public async Task Create_PostCommitReadFailureReturnsReceiptAndReadRetryDoesNotReplayWrite()
    {
        // Part登记创建在唯一事务内Flush，首次持久化读取就是提交后的呈现读取。
        var fault = new PostCommitReadFault { ReadsBeforeFailure = 0 };
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        fault.Arm = true;
        var receipt = await CreatePartAsync(fixture, admin, new { ECODE = "READ-FAULT", ENAME = "写后读失败" });
        Assert.Single(receipt.EnumerateObject()); Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.True(fault.Triggered);
        Assert.False(fault.FailedInsideTransaction);
        fault.Enabled = false;
        for (var retry = 0; retry < 2; retry++)
            Assert.Equal(1, (await OwnedResourceReadHttpRequests.ReadAsync(fixture, admin, Entry(fixture), "part.part", "list", "/api/part/parts?keyword=READ-FAULT")).GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "零组件规范重读在唯一事务内，读取失败不得遗留已提交实体")]
    public async Task Create_NormalizationReadFailureRollsBackTheInsertedEntity()
    {
        var fault = new PostCommitReadFault { ReadsBeforeFailure = 0 };
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        fault.Arm = true;
        using var response = await SendCreateAsync(fixture, admin, new { ECODE = "ROLLBACK", ENAME = "规范重读失败" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(fault.Triggered);
        fault.Enabled = false;
        Assert.Equal(0, (await OwnedResourceReadHttpRequests.ReadAsync(fixture, admin, Entry(fixture), "part.part", "list", "/api/part/parts?keyword=ROLLBACK")).GetProperty("total").GetInt32());
        Assert.True(fault.FailedInsideTransaction);
    }

    // 已有正向旅程使用实际编译声明；缺声明反例由PartWriteExecutionSqlServerTests直接构造请求。
    private static ActionDataAccessRequirement CreateDeclaration(BusinessAuditHostFixture fixture)
        => OwnedResourceReadHttpRequests.Declaration(fixture, Create(fixture), "part.part", "create");

    private static Task<HttpResponseMessage> SendCreateAsync(BusinessAuditHostFixture fixture, HttpClient client, object values)
        => FeatureHttpTestRequests.SendAsync(client, Create(fixture), HttpMethod.Post, "/api/part/parts", values, CreateDeclaration(fixture));

    private static async Task<JsonElement> CreatePartAsync(BusinessAuditHostFixture fixture, HttpClient client, object values)
    {
        using var response = await SendCreateAsync(fixture, client, values);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static FeaturePoint Entry(BusinessAuditHostFixture fixture) => Assert.Single(fixture.FeatureCatalog, p => p.ModuleId == "Part" && p.Kind == "navigation" && p.Key == "part.parts");
    private static FeaturePoint Create(BusinessAuditHostFixture fixture) => Assert.Single(fixture.FeatureCatalog, p => p.BehaviorKey == "part.part.create");
    private static async Task RuleAsync(BusinessAuditHostFixture fixture, HttpClient admin, string key, string field, string value, string action)
    {
        var rule = new DataScopeRuleDto(key, key, null, key, true, "part.part",
            new("attribute", field, "equal", JsonSerializer.SerializeToElement(value)),
            [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = "deny" })], []);
        var save = Assert.Single(fixture.FeatureCatalog, p => p.BehaviorKey == "authorization.dataScope.save");
        using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    // 编码预检不得把整张真实 Part 表物化到应用；计数和标准数据库分页不是无界明细读取。
    private sealed class PartScanObserver : DbCommandInterceptor
    {
        public System.Collections.Concurrent.ConcurrentBag<string> UnboundedReads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            if (sql.Contains("[Part]", StringComparison.Ordinal) && sql.StartsWith("SELECT ", StringComparison.Ordinal)
                && !sql.Contains("COUNT(", StringComparison.Ordinal) && !sql.Contains("WHERE", StringComparison.Ordinal)
                && !sql.Contains("OFFSET", StringComparison.Ordinal)) UnboundedReads.Add(sql);
            return ValueTask.FromResult(result);
        }
    }

    // 标准默认仓储插入后先规范化重读一次；随后对象读取裁决才是可降级的写后展示边界。
    private sealed class PostCommitReadFault : DbCommandInterceptor
    {
        public bool Arm; public bool Enabled;
        public bool Triggered;
        public int ReadsBeforeFailure { get; init; } = 1;
        public bool FailedInsideTransaction;
        private int _reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("SELECT", StringComparison.Ordinal) && command.CommandText.Contains("[Part]", StringComparison.Ordinal)
                && ++_reads > ReadsBeforeFailure)
            {
                Triggered = true;
                FailedInsideTransaction = eventData.Context?.Database.CurrentTransaction is not null;
                throw new InvalidOperationException("Injected Part post-commit read failure.");
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Arm && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal) && command.CommandText.Contains("[Part]", StringComparison.Ordinal))
            { Arm = false; Enabled = true; }
            return ValueTask.FromResult(result);
        }
    }
}
