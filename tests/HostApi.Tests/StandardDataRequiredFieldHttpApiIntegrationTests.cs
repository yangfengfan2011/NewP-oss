using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.Kernel.PluginPackages;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Routing;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class StandardDataRequiredFieldHttpApiIntegrationTests : IDisposable
{
    private IReadOnlyList<FeaturePoint> _points = [];
    private FeaturePoint Point(string? action = null) => _points.Single(point => action is null
        ? point.BehaviorKey == "Partner.AssetCatalog.Asset.read"
        : point.Key == action && point.Kind == "action");

    [Fact]
    public async Task StandardDataDetail_AutomaticallyProjectsAuditTimeAndRejectsUnselectedCompanionWrites()
    {
        var repository = new RecordingRepository();
        var instant = new DateTime(2026, 9, 4, 7, 24, 26, DateTimeKind.Utc).AddTicks(5023291);
        repository.Rows.Add(new Dictionary<string, object?> { ["EID"] = "Asset_1", ["Name"] = "Asset", ["SerialNo"] = "001", ["CreatedAt"] = instant, ["UpdatedAt"] = instant });
        await using var app = BuildApplication(repository, auditProjection: true);
        await app.StartAsync();
        using var client = app.GetTestClient();
        var result = await FeatureHttpTestRequests.ReadAsync(client, Point(), "/api/standard/partner-asset-catalog/assets/Asset_1");
        var fields = result.GetProperty("fields");
        Assert.Equal(instant, fields.GetProperty("CreatedAt").GetDateTime());
        Assert.Equal("2026/9/4 15:24:26", fields.GetProperty("CreatedAt_DisplayValue").GetString());
        Assert.False(fields.TryGetProperty("CreatedBy_DisplayValue", out _));
        var rejected = await FeatureHttpTestRequests.SendAsync(client, Point("create"), HttpMethod.Post, "/api/standard/partner-asset-catalog/assets", new { Name = "Other", CreatedBy_DisplayValue = "forged" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(0, repository.CreateCallCount);
    }

    private sealed class Presentation : ICP.Platform.Presentation.IRequestPresentationContextAccessor
    {
        public ICP.Platform.Presentation.RequestPresentationContext? Current => new("zh-CN", "Asia/Taipei");
    }
    private readonly string _packageRoot = Path.Combine(
        Path.GetTempPath(),
        "newp-standard-data-required-api-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateApi_RejectsMissingNullOrBlankRequiredValueWithoutWriting()
    {
        var repository = new RecordingRepository();
        await using var app = BuildApplication(repository);
        await app.StartAsync();
        var client = app.GetTestClient();

        var missingResponse = await FeatureHttpTestRequests.SendAsync(client, Point("create"), HttpMethod.Post,
            "/api/standard/partner-asset-catalog/assets",
            new Dictionary<string, object?> { ["SerialNo"] = "S-001" });
        var nullResponse = await FeatureHttpTestRequests.SendAsync(client, Point("create"), HttpMethod.Post,
            "/api/standard/partner-asset-catalog/assets",
            new Dictionary<string, object?> { ["Name"] = null, ["SerialNo"] = "S-002" });
        var blankResponse = await FeatureHttpTestRequests.SendAsync(client, Point("create"), HttpMethod.Post,
            "/api/standard/partner-asset-catalog/assets",
            new Dictionary<string, object?> { ["Name"] = "   ", ["SerialNo"] = "S-003" });

        await AssertRequiredErrorAsync(missingResponse);
        await AssertRequiredErrorAsync(nullResponse);
        await AssertRequiredErrorAsync(blankResponse);
        Assert.Equal(0, repository.CreateCallCount);
        Assert.Empty(repository.Rows);
    }

    [Fact]
    public async Task UpdateApi_PreservesOmittedRequiredValueAndRejectsExplicitEmptyValuesWithoutWriting()
    {
        var repository = new RecordingRepository();
        await using var app = BuildApplication(repository);
        await app.StartAsync();
        var client = app.GetTestClient();
        var createdResponse = await FeatureHttpTestRequests.SendAsync(client, Point("create"), HttpMethod.Post,
            "/api/standard/partner-asset-catalog/assets",
            new Dictionary<string, object?> { ["Name"] = "正式资产", ["SerialNo"] = "S-004" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var row = Assert.Single(repository.Rows);
        var id = Assert.IsType<string>(row["EID"]);

        var omittedResponse = await FeatureHttpTestRequests.SendAsync(client, Point("edit"), HttpMethod.Put,
            $"/api/standard/partner-asset-catalog/assets/{id}",
            new Dictionary<string, object?> { ["SerialNo"] = "S-005" });

        Assert.Equal(HttpStatusCode.OK, omittedResponse.StatusCode);
        Assert.Equal("正式资产", row["Name"]);
        Assert.Equal("S-005", row["SerialNo"]);

        var nullResponse = await FeatureHttpTestRequests.SendAsync(client, Point("edit"), HttpMethod.Put,
            $"/api/standard/partner-asset-catalog/assets/{id}",
            new Dictionary<string, object?> { ["Name"] = null });
        var blankResponse = await FeatureHttpTestRequests.SendAsync(client, Point("edit"), HttpMethod.Put,
            $"/api/standard/partner-asset-catalog/assets/{id}",
            new Dictionary<string, object?> { ["Name"] = "  " });

        await AssertRequiredErrorAsync(nullResponse);
        await AssertRequiredErrorAsync(blankResponse);
        Assert.Equal("正式资产", row["Name"]);
        Assert.Equal("S-005", row["SerialNo"]);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task StandardData_UsesIndependentEnablePositionAndPreservesDeclaredEditFields()
    {
        var repository = new RecordingRepository();
        await using var app = BuildApplication(repository);
        await app.StartAsync();
        using var client = app.GetTestClient();
        var body = new { Name = "原名称", SerialNo = "STATE", IsEnabled = false };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/standard/partner-asset-catalog/assets", body)).StatusCode);
        using var created = await FeatureHttpTestRequests.SendAsync(client, Point("create"), HttpMethod.Post, "/api/standard/partner-asset-catalog/assets", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = Assert.Single(repository.Rows);
        var path = "/api/standard/partner-asset-catalog/assets/" + row["EID"];
        Assert.Equal(HttpStatusCode.Forbidden, (await FeatureHttpTestRequests.SendAsync(client, Point("disable"), HttpMethod.Post, path + "/enable")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FeatureHttpTestRequests.SendAsync(client, Point("enable"), HttpMethod.Post, path + "/enable", new { Name = "不得写入", IsEnabled = false })).StatusCode);
        Assert.Equal("原名称", row["Name"]);
        Assert.Equal(true, row["IsEnabled"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await FeatureHttpTestRequests.SendAsync(client, Point("enable"), HttpMethod.Put, path, new { Name = "越权" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FeatureHttpTestRequests.SendAsync(client, Point("edit"), HttpMethod.Put, path, new { Name = "合法编辑", IsEnabled = false })).StatusCode);
        Assert.Equal("合法编辑", row["Name"]);
        Assert.Equal(false, row["IsEnabled"]);
        Assert.Equal(HttpStatusCode.OK, (await FeatureHttpTestRequests.SendAsync(client, Point("disable"), HttpMethod.Post, path + "/disable")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await FeatureHttpTestRequests.SendAsync(client, Point(), HttpMethod.Delete, path)).StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_packageRoot))
        {
            Directory.Delete(_packageRoot, recursive: true);
        }
    }

    private WebApplication BuildApplication(RecordingRepository repository, bool auditProjection = false)
    {
        var descriptor = BuildDescriptor();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("StandardDataApiTests").AddCookie("StandardDataApiTests", options =>
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; });
        builder.Services.AddPlatformAuthorization();
        builder.Services.AddAuthorization(options => options.AddPolicy(DenyAllAttribute.PolicyName, policy => policy.RequireAssertion(_ => false)));
        builder.Services.AddSingleton<IFeatureDecisionSource>(new DeclaredFeatures(_points));
        builder.Services.AddStandardCrudKernel();
        builder.Services.AddSingleton<IStandardCrudRepository>(repository);
        builder.Services.AddSingleton<IPlatformQueryExecutor, UnusedQueryExecutor>();
        builder.Services.AddStandardCrudResource(descriptor);
        if (auditProjection)
        {
            var model = new ICP.Platform.Modeling.Projection.ModelMetadataProjector().Project(new ICP.Platform.Modeling.Metadata.ModelMetadataDocument
            {
                ModuleId = descriptor.ModuleId,
                Model = new ICP.Platform.Modeling.Metadata.ModelMetadataModel { Name = descriptor.ModelName, Kind = "Entity" }
            }) with { OwnerModuleId = descriptor.ModuleId };
            builder.Services.AddSingleton(new ICP.Platform.Persistence.Planning.CompiledPersistencePlan("candidate", "snapshot", "hash", default, "digest", [], [model], []));
            builder.Services.AddSingleton<ICP.Platform.Presentation.ITemporalDisplayProjectionExecutor>(new ICP.Platform.Presentation.TemporalDisplayProjectionExecutor(new Presentation(), new ICP.Platform.Presentation.TemporalDisplayFormatter()));
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "standard-data-api-test-subject")],
                "StandardDataApiTests"));
            await next();
        });
        app.UseAuthorization();
        app.MapStandardCrudResources();
        FeatureEndpointBindings.Validate(((IEndpointRouteBuilder)app).DataSources, _points);
        return app;
    }

    private StandardCrudDescriptor BuildDescriptor()
    {
        WritePackageContracts();
        var documents = StandardDataMetadataProjection.Project("Partner.AssetCatalog",
            JsonNode.Parse(File.ReadAllText(Path.Combine(_packageRoot, "authoring/newp.plugin.json")))!.AsObject(),
            JsonNode.Parse(File.ReadAllText(Path.Combine(_packageRoot, "projections/standard-data.json")))!.AsObject(),
            new Dictionary<string, JsonObject>());
        _points = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Partner.AssetCatalog",
            documents.Values.Select(document => JsonSerializer.SerializeToElement(document))));
        var plan = new CanonicalModulePlan(
            "candidate",
            "1.0",
            "plan-hash",
            "snapshot",
            [],
            [CreateModule()]);
        return Assert.Single(new StandardDataCrudDescriptorProjector().Project(plan));
    }

    private CanonicalModulePlanModule CreateModule() => new(
        "Partner.AssetCatalog",
        "1.0.0",
        "Asset Catalog",
        null,
        _packageRoot,
        null,
        null,
        "projections/standard-data.json",
        "standard-data.v1",
        "standard-data",
        [],
        ["/api/standard/partner-asset-catalog/assets", "/assets"],
        ["Partner.AssetCatalog.Asset"],
        ["Partner.AssetCatalog.Asset"],
        [],
        [],
        ["Asset"],
        new ModulePlanProvenance("installed-package", Path.Combine(_packageRoot, "plugin.json"), "/"));

    private void WritePackageContracts()
    {
        Directory.CreateDirectory(Path.Combine(_packageRoot, "authoring"));
        Directory.CreateDirectory(Path.Combine(_packageRoot, "projections"));
        var blueprint = new StandardPluginBlueprint(
            "1.0",
            "standard-data.v1",
            "Partner.AssetCatalog",
            "Asset Catalog",
            "1.0.0",
            null,
            new StandardPluginCompatibility(
                new PlatformApiVersionRange("1.0.0", "2.0.0"),
                new StandardPersistenceCompatibility("ef-migrations.v1", ["sqlServer"])),
            new StandardDataBlueprint(
            [
                new StandardDataResourceBlueprint(
                    "Asset",
                    "Assets",
                    "Asset",
                    "Name",
                    [
                        new StandardDataFieldBlueprint("Name", "string", Required: true),
                        new StandardDataFieldBlueprint("SerialNo", "string", Required: false, Unique: true),
                        new StandardDataFieldBlueprint("IsEnabled", "boolean", Required: false)
                    ])
            ]),
            new StandardNavigationBlueprint("business", "Assets", "Assets", 200),
            new StandardPersistenceBlueprint([], new Dictionary<string, StandardMigrationBundleBlueprint>()));
        var projection = new StandardPluginProjection(
            "partner-asset-catalog",
            [
                new StandardDataResourceProjection(
                    "Asset",
                    "/api/standard/partner-asset-catalog/assets",
                    "/assets",
                    "np_partner_asset_catalog",
                    "assets",
                    ["UX_assets_SerialNo"],
                    [])
            ]);
        var manifest = new StandardPluginPackageManifest(
            1,
            "standard-data.v1",
            "Partner.AssetCatalog",
            "Asset Catalog",
            "1.0.0",
            null,
            new PlatformApiAttestation(
                "1.0.0",
                "1.0.0",
                "2.0.0",
                "sha256:" + new string('0', 64),
                "standard-data.v1",
                "ef-migrations.v1",
                ["sqlServer"]),
            new StandardPluginPackageArtifacts(
                "authoring/newp.plugin.json",
                "projections/standard-data.json",
                "provenance/explain.json"),
            blueprint.Persistence,
            new StandardPluginPackageChecksums("checksums/sha256sums.json"));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

        File.WriteAllText(Path.Combine(_packageRoot, "plugin.json"), JsonSerializer.Serialize(manifest, options));
        File.WriteAllText(
            Path.Combine(_packageRoot, "authoring", "newp.plugin.json"),
            JsonSerializer.Serialize(blueprint, options));
        File.WriteAllText(
            Path.Combine(_packageRoot, "projections", "standard-data.json"),
            JsonSerializer.Serialize(projection, options));
    }

    private static async Task AssertRequiredErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Partner.AssetCatalog.Asset.NameRequired", error.GetProperty("code").GetString());
        Assert.Equal("Partner.AssetCatalog.Asset.Name.required", error.GetProperty("reasonKey").GetString());
    }

    private sealed class DeclaredFeatures(IReadOnlyList<FeaturePoint> points) : IFeatureDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string behaviorKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(points.Any(point => point.Position == position && point.BehaviorKey == behaviorKey)
                ? PermissionDecision.Granted : PermissionDecision.Denied);
    }

    private sealed class UnusedQueryExecutor : IPlatformQueryExecutor
    {
        public Task<PlatformQueryResult> QueryAsync(
            PlatformQueryDescriptor descriptor,
            PlatformQueryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRepository : IStandardCrudRepository
    {
        public List<Dictionary<string, object?>> Rows { get; } = [];

        public int CreateCallCount { get; private set; }

        public int UpdateCallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default)
        {
            var row = Rows.SingleOrDefault(candidate =>
                string.Equals(candidate[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(row);
        }

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudUniqueConstraint constraint,
            IReadOnlyDictionary<string, object?> equalityValues,
            string? excludedId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            var row = new Dictionary<string, object?>(values, StringComparer.Ordinal)
            {
                [descriptor.IdField] = $"Asset_{Guid.NewGuid():N}"
            };
            Rows.Add(row);
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(row);
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            var row = Rows.SingleOrDefault(candidate =>
                string.Equals(candidate[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
            if (row is null)
            {
                return Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);
            }

            UpdateCallCount++;
            foreach (var pair in values)
            {
                row[pair.Key] = pair.Value;
            }

            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(row);
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            UpdateAsync(descriptor, id, values, cancellationToken);

        public Task<bool> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
