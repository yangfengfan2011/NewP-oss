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
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Example.Crud;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ExampleRequiredFieldHttpApiIntegrationTests
{
    [Fact]
    public async Task ExampleRecordCreateApi_RejectsMissingSourceAndPersistsExplicitSource()
    {
        var repository = new RecordingRepository();
        await using var app = BuildApplication(repository);
        await app.StartAsync();
        var client = app.GetTestClient();

        var missingResponse = await client.PostAsJsonAsync(
            "/api/example/records",
            new Dictionary<string, object?>
            {
                ["ECODE"] = "API-001",
                ["ENAME"] = "缺少来源"
            });

        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        var problem = await missingResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Example.Record.SourceRequired", problem.GetProperty("code").GetString());
        Assert.Empty(repository.Rows);

        var createdResponse = await client.PostAsJsonAsync(
            "/api/example/records",
            new Dictionary<string, object?>
            {
                ["ECODE"] = "API-002",
                ["ENAME"] = "显式来源",
                ["RecordSource"] = "Manual"
            });

        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var row = Assert.Single(repository.Rows);
        Assert.Equal("API-002", row["ECODE"]);
        Assert.Equal("Manual", row["RecordSource"]);
    }

    private static WebApplication BuildApplication(RecordingRepository repository)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformAuthorization();
        builder.Services.AddSingleton<IPermissionDecisionSource, GrantAllPermissionDecisionSource>();
        builder.Services.AddStandardCrudKernel();
        builder.Services.AddSingleton<IStandardCrudRepository>(repository);
        builder.Services.AddSingleton<IPlatformQueryExecutor, UnusedQueryExecutor>();
        builder.Services.AddSingleton<IIdentityAccountDirectory, EmptyIdentityAccountDirectory>();
        builder.Services.AddSingleton<IStandardCrudDisplayValueResolver, ExampleRecordDisplayValueResolver>();
        builder.Services.AddStandardCrudResource(ExampleRecordCrudDescriptorFactory.Create());

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "example-api-test-subject")],
                "ExampleApiTests"));
            await next();
        });
        app.UseAuthorization();
        app.MapStandardCrudResources();
        return app;
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

    private sealed class EmptyIdentityAccountDirectory : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(
            string accountEid,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IdentityAccountSnapshot?>(null);

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, IdentityAccountSnapshot>>(
                new Dictionary<string, IdentityAccountSnapshot>(StringComparer.Ordinal));

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<IdentityAccountSnapshot>>([]);
    }

    private sealed class RecordingRepository : IStandardCrudRepository
    {
        public List<Dictionary<string, object?>> Rows { get; } = [];

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
            var row = new Dictionary<string, object?>(values, StringComparer.Ordinal)
            {
                [descriptor.IdField] = $"ExampleRecord_{Guid.NewGuid():N}"
            };
            Rows.Add(row);
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(row);
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
