using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Extensions;
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

        var missingResponse = await client.PostAsJsonAsync(
            "/api/standard/partner-asset-catalog/assets",
            new Dictionary<string, object?> { ["SerialNo"] = "S-001" });
        var nullResponse = await client.PostAsJsonAsync(
            "/api/standard/partner-asset-catalog/assets",
            new Dictionary<string, object?> { ["Name"] = null, ["SerialNo"] = "S-002" });
        var blankResponse = await client.PostAsJsonAsync(
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
        var createdResponse = await client.PostAsJsonAsync(
            "/api/standard/partner-asset-catalog/assets",
            new Dictionary<string, object?> { ["Name"] = "正式资产", ["SerialNo"] = "S-004" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var row = Assert.Single(repository.Rows);
        var id = Assert.IsType<string>(row["EID"]);

        var omittedResponse = await client.PutAsJsonAsync(
            $"/api/standard/partner-asset-catalog/assets/{id}",
            new Dictionary<string, object?> { ["SerialNo"] = "S-005" });

        Assert.Equal(HttpStatusCode.OK, omittedResponse.StatusCode);
        Assert.Equal("正式资产", row["Name"]);
        Assert.Equal("S-005", row["SerialNo"]);

        var nullResponse = await client.PutAsJsonAsync(
            $"/api/standard/partner-asset-catalog/assets/{id}",
            new Dictionary<string, object?> { ["Name"] = null });
        var blankResponse = await client.PutAsJsonAsync(
            $"/api/standard/partner-asset-catalog/assets/{id}",
            new Dictionary<string, object?> { ["Name"] = "  " });

        await AssertRequiredErrorAsync(nullResponse);
        await AssertRequiredErrorAsync(blankResponse);
        Assert.Equal("正式资产", row["Name"]);
        Assert.Equal("S-005", row["SerialNo"]);
        Assert.Equal(1, repository.UpdateCallCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_packageRoot))
        {
            Directory.Delete(_packageRoot, recursive: true);
        }
    }

    private WebApplication BuildApplication(RecordingRepository repository)
    {
        var descriptor = BuildDescriptor();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformAuthorization();
        builder.Services.AddSingleton<IPermissionDecisionSource, GrantAllPermissionDecisionSource>();
        builder.Services.AddStandardCrudKernel();
        builder.Services.AddSingleton<IStandardCrudRepository>(repository);
        builder.Services.AddSingleton<IPlatformQueryExecutor, UnusedQueryExecutor>();
        builder.Services.AddStandardCrudResource(descriptor);

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
        return app;
    }

    private StandardCrudDescriptor BuildDescriptor()
    {
        WritePackageContracts();
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
        [
            "Partner.AssetCatalog.Asset.View",
            "Partner.AssetCatalog.Asset.Create",
            "Partner.AssetCatalog.Asset.Update",
            "Partner.AssetCatalog.Asset.Delete"
        ],
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
                        new StandardDataFieldBlueprint("SerialNo", "string", Required: false, Unique: true)
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
                    [
                        "Partner.AssetCatalog.Asset.View",
                        "Partner.AssetCatalog.Asset.Create",
                        "Partner.AssetCatalog.Asset.Update",
                        "Partner.AssetCatalog.Asset.Delete"
                    ])
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
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

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

    private sealed class GrantAllPermissionDecisionSource : IPermissionDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PermissionDecision.Granted);
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
