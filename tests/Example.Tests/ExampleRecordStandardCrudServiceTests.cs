using System.IO.Compression;
using System.Text;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Example.Crud;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ICP.Platform.Modules.Example.Tests;

public sealed class ExampleRecordStandardCrudServiceTests
{
    [Fact]
    public async Task ListAsync_PreservesRawReviewerUserAndResolvesDisplayValueInOneBatch()
    {
        var descriptor = ExampleCrudTestResources.Create();
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
            [
                new Dictionary<string, object?>
                {
                    ["EID"] = "record-1",
                    ["ECODE"] = "R001",
                    ["ReviewerUserEID"] = "User_A"
                }
            ],
            Total: 1,
            Page: 1,
            PageSize: 20,
            SortBy: "ECODE",
            SortDirection: "asc",
            ResponseFields: ["EID", "ECODE", "ReviewerUserEID", "ReviewerUserEID_DisplayValue"],
            RequestedDisplayFields: descriptor.DisplayValueFields));
        var directory = new RecordingAccountDirectory([Account("User_A", "Alice")]);
        var service = CreateService(new InMemoryStandardCrudRepository([]), executor, directory);

        var result = await service.ListAsync(
            descriptor,
            new StandardCrudListQuery(Fields: ["EID", "ECODE", "ReviewerUserEID"]));

        Assert.Equal(["EID", "ECODE", "ReviewerUserEID", "ReviewerUserEID_DisplayValue"], executor.LastRequest!.Fields);
        var row = Assert.Single(result.Items);
        Assert.Equal("User_A", row["ReviewerUserEID"]);
        Assert.Equal("Alice", row["ReviewerUserEID_DisplayValue"]);
        Assert.Equal(1, directory.FindManyCallCount);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("all")]
    public async Task ExportAsync_WhenReviewerUserColumnRequested_WritesResolvedDisplayWithoutLeakingRawEid(string mode)
    {
        var descriptor = ExampleCrudTestResources.Create();
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
            [
                new Dictionary<string, object?>
                {
                    ["EID"] = "record-1",
                    ["ReviewerUserEID"] = "User_issue137_raw"
                }
            ],
            Total: 1,
            Page: 1,
            PageSize: 20,
            SortBy: null,
            SortDirection: null,
            ResponseFields: ["ReviewerUserEID", "ReviewerUserEID_DisplayValue"],
            RequestedDisplayFields: descriptor.DisplayValueFields));
        var directory = new RecordingAccountDirectory([Account("User_issue137_raw", "Issue 137 Reviewer")]);
        var service = CreateService(new InMemoryStandardCrudRepository([]), executor, directory);
        var exportService = new StandardCrudExportService(
            service,
            new StandardCrudXlsxWriter(),
            new ConfigurationBuilder().Build());

        var file = await exportService.ExportAsync(
            descriptor,
            new StandardCrudExportRequest(
                mode,
                [new StandardCrudExportColumn("reviewerUserEid", "ReviewerUserEID", "复核账号")],
                new StandardCrudExportListQuery(PageIndex: 1, PageSize: 20)));

        Assert.Equal(["ReviewerUserEID", "ReviewerUserEID_DisplayValue"], executor.LastRequest!.Fields);
        Assert.Equal(1, directory.FindManyCallCount);
        var worksheet = ReadWorksheetXml(file.Content);
        Assert.Contains("复核账号", worksheet, StringComparison.Ordinal);
        Assert.Contains("Issue 137 Reviewer", worksheet, StringComparison.Ordinal);
        Assert.DoesNotContain("User_issue137_raw", worksheet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDetailAsync_WhenReviewerAccountMissing_PreservesRawFieldAndFallsBackToRawEid()
    {
        var descriptor = ExampleCrudTestResources.Create();
        var repository = new InMemoryStandardCrudRepository(
            [
                new Dictionary<string, object?>
                {
                    ["EID"] = "record-1",
                    ["ECODE"] = "R001",
                    ["ENAME"] = "记录一",
                    ["ReviewerUserEID"] = "User_Deleted"
                }
            ]);
        var directory = new RecordingAccountDirectory([]);
        var service = CreateService(repository, EmptyQueryExecutor(), directory);

        var detail = await service.GetDetailAsync(descriptor, "record-1");

        Assert.NotNull(detail);
        Assert.Equal("User_Deleted", detail!.Fields["ReviewerUserEID"]);
        Assert.Equal("User_Deleted", detail.Fields["ReviewerUserEID_DisplayValue"]);
        Assert.Equal(1, directory.FindManyCallCount);
    }

    [Fact]
    public async Task CreateAsync_ReturnsRawReviewerUserAndResolvedDisplayValue()
    {
        var descriptor = ExampleCrudTestResources.Create();
        var repository = new RecordingMutationStandardCrudRepository([]);
        var directory = new RecordingAccountDirectory([Account("User_A", "Alice")]);
        var service = CreateService(repository, EmptyQueryExecutor(), directory);

        var result = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?>
            {
                ["ECODE"] = "R001",
                ["ENAME"] = "记录一",
                ["ReviewerUserEID"] = "User_A",
                ["RecordSource"] = "Manual"
            });

        Assert.True(result.Succeeded);
        Assert.Equal("User_A", result.Detail!.Fields["ReviewerUserEID"]);
        Assert.Equal("Alice", result.Detail.Fields["ReviewerUserEID_DisplayValue"]);
        Assert.Equal("Manual", result.Detail.Fields["RecordSource"]);
        Assert.Equal(1, repository.CreateCallCount);
        Assert.Equal(1, directory.FindManyCallCount);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsRawReviewerUserAndResolvedDisplayValue()
    {
        var descriptor = ExampleCrudTestResources.Create();
        var createdAt = new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc);
        var repository = new RecordingMutationStandardCrudRepository(
            [
                new Dictionary<string, object?>
                {
                    ["EID"] = "record-1",
                    ["ECODE"] = "R001",
                    ["ENAME"] = "旧名称",
                    ["ReviewerUserEID"] = "User_A",
                    ["RecordSource"] = "Legacy",
                    ["CreatedBy"] = "creator",
                    ["CreatedAt"] = createdAt,
                    ["UpdatedBy"] = "creator",
                    ["UpdatedAt"] = createdAt
                }
            ]);
        var directory = new RecordingAccountDirectory([Account("User_B", "Bob")]);
        var service = CreateService(repository, EmptyQueryExecutor(), directory);

        var result = await service.UpdateAsync(
            descriptor,
            "record-1",
            new Dictionary<string, object?>
            {
                ["ENAME"] = "新名称",
                ["ReviewerUserEID"] = "User_B"
            });

        Assert.True(result.Succeeded);
        Assert.Equal("User_B", result.Detail!.Fields["ReviewerUserEID"]);
        Assert.Equal("Bob", result.Detail.Fields["ReviewerUserEID_DisplayValue"]);
        Assert.Equal("Legacy", result.Detail.Fields["RecordSource"]);
        Assert.Equal("creator", result.Detail.Fields["CreatedBy"]);
        Assert.Equal(createdAt, result.Detail.Fields["CreatedAt"]);
        Assert.Equal(SystemEntityAuditStampProvider.SystemActorId, result.Detail.Fields["UpdatedBy"]);
        Assert.True((DateTime)result.Detail.Fields["UpdatedAt"]! >= createdAt);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(1, directory.FindManyCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenRecordSourceIsMissing_ReturnsStableRequiredErrorWithoutWriting()
    {
        var descriptor = ExampleCrudTestResources.Create();
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = CreateService(repository, EmptyQueryExecutor(), new RecordingAccountDirectory([]));

        var result = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?>
            {
                ["ECODE"] = "R002",
                ["ENAME"] = "缺少来源"
            });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Equal("Example.Record.SourceRequired", result.Error.Code);
        Assert.Equal("example.record.sourceRequired", result.Error.ReasonKey);
        Assert.Equal(0, repository.CreateCallCount);
    }

    [Fact]
    public async Task UpdateAsync_WhenRecordSourceIsBlank_ReturnsStableRequiredErrorWithoutWriting()
    {
        var descriptor = ExampleCrudTestResources.Create();
        var repository = new RecordingMutationStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "record-2",
                ["ECODE"] = "R002",
                ["ENAME"] = "记录二",
                ["RecordSource"] = "Legacy"
            }
        ]);
        var service = CreateService(repository, EmptyQueryExecutor(), new RecordingAccountDirectory([]));

        var result = await service.UpdateAsync(
            descriptor,
            "record-2",
            new Dictionary<string, object?> { ["RecordSource"] = "   " });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Equal("Example.Record.SourceRequired", result.Error.Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenCanonicalDisplayCompanionIsSubmitted_ReusesReadOnlyWriteReject()
    {
        var descriptor = ExampleCrudTestResources.Create();
        var repository = new RecordingMutationStandardCrudRepository([]);
        var directory = new RecordingAccountDirectory([Account("User_A", "Alice")]);
        var service = CreateService(repository, EmptyQueryExecutor(), directory);

        var result = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?>
            {
                ["ECODE"] = "R001",
                ["ENAME"] = "记录一",
                ["ReviewerUserEID"] = "User_A",
                ["ReviewerUserEID_DisplayValue"] = "伪造显示名"
            });

        Assert.False(result.Succeeded);
        Assert.Equal(0, repository.CreateCallCount);
        var error = Assert.IsType<StandardCrudError>(result.Error);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("StandardCrud.DisplayValueFieldReadOnly", error.Code);
        Assert.Equal("standardCrud.displayValueFieldReadOnly", error.ReasonKey);
        var fieldError = Assert.Single(error.FieldErrors);
        Assert.Equal("ReviewerUserEID_DisplayValue", fieldError.Field);
    }

    private static StandardCrudService CreateService(
        IStandardCrudRepository repository,
        IPlatformQueryExecutor executor,
        RecordingAccountDirectory directory) =>
        new(
            repository,
            executor,
            [],
            [new ExampleRecordDisplayValueResolver(directory)],
            [],
            [],
            [],
            [],
            auditStampProvider: new SystemEntityAuditStampProvider(TimeProvider.System),
            temporalDisplayProjectionExecutor: new ICP.Platform.Presentation.TemporalDisplayProjectionExecutor(
                new NoPresentationContext(), new ICP.Platform.Presentation.TemporalDisplayFormatter()));

    private sealed class NoPresentationContext : ICP.Platform.Presentation.IRequestPresentationContextAccessor
    {
        public ICP.Platform.Presentation.RequestPresentationContext? Current => null;
    }

    private static RecordingPlatformQueryExecutor EmptyQueryExecutor() =>
        new(new PlatformQueryResult([], 0, 1, 20, null, null, [], []));

    private static IdentityAccountSnapshot Account(string accountEid, string name) => new()
    {
        AccountEid = accountEid,
        ECODE = accountEid.ToLowerInvariant(),
        ENAME = name,
        Status = IdentityAccountStatus.Enabled,
        Revision = 3,
        IsLocalAdministrator = false,
        IsPersonnelBound = false,
        PersonnelDisableHold = false
    };

    private static string ReadWorksheetXml(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var worksheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        using var reader = new StreamReader(worksheet, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class RecordingAccountDirectory(IReadOnlyList<IdentityAccountSnapshot> accounts) : IIdentityAccountDirectory
    {
        private readonly IReadOnlyDictionary<string, IdentityAccountSnapshot> _accounts =
            accounts.ToDictionary(account => account.AccountEid, StringComparer.Ordinal);

        public int FindManyCallCount { get; private set; }

        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_accounts.TryGetValue(accountEid, out var account) ? account : null);

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default)
        {
            FindManyCallCount++;
            var requested = accountEids
                .Where(static accountEid => !string.IsNullOrWhiteSpace(accountEid))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyDictionary<string, IdentityAccountSnapshot>>(
                requested
                    .Where(_accounts.ContainsKey)
                    .ToDictionary(accountEid => accountEid, accountEid => _accounts[accountEid], StringComparer.Ordinal));
        }

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingMutationStandardCrudRepository(IEnumerable<Dictionary<string, object?>> rows)
        : IStandardCrudRepository
    {
        private readonly InMemoryStandardCrudRepository _inner = new(rows);

        public int CreateCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            _inner.GetByIdAsync(descriptor, id, cancellationToken);

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            _inner.OptionsAsync(descriptor, query, cancellationToken);

        public Task<bool> ExistsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudUniqueConstraint constraint,
            IReadOnlyDictionary<string, object?> equalityValues,
            string? excludedId = null,
            CancellationToken cancellationToken = default) =>
            _inner.ExistsAsync(descriptor, constraint, equalityValues, excludedId, cancellationToken);

        public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            return await _inner.CreateAsync(descriptor, values, cancellationToken);
        }

        public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            return await _inner.UpdateAsync(descriptor, id, values, cancellationToken);
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            _inner.UpdateAsync(descriptor, id, values, expectedRevision, cancellationToken);

        public Task<bool> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(descriptor, id, cancellationToken);
    }

    private sealed class InMemoryStandardCrudRepository(IEnumerable<Dictionary<string, object?>> rows) : IStandardCrudRepository
    {
        private readonly List<Dictionary<string, object?>> _rows =
            rows.Select(row => new Dictionary<string, object?>(row, StringComparer.Ordinal)).ToList();

        public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default)
        {
            var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(row is null ? null : Project(row, descriptor.FieldSet.DetailFields));
        }

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<StandardCrudOptionItem> options = _rows.Select(row => new StandardCrudOptionItem
            {
                Value = row[descriptor.IdField]?.ToString() ?? string.Empty,
                Label = string.Join(
                    " ",
                    descriptor.OptionLabelFields
                        .Select(field => row.TryGetValue(field, out var value) ? value?.ToString() : null)
                        .Where(value => !string.IsNullOrWhiteSpace(value)))
            }).ToList();

            return Task.FromResult(options);
        }

        public Task<bool> ExistsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudUniqueConstraint constraint,
            IReadOnlyDictionary<string, object?> equalityValues,
            string? excludedId = null,
            CancellationToken cancellationToken = default)
        {
            var exists = _rows.Any(row =>
                (string.IsNullOrWhiteSpace(excludedId)
                    || !string.Equals(row[descriptor.IdField]?.ToString(), excludedId, StringComparison.Ordinal))
                && equalityValues.All(pair => row.TryGetValue(pair.Key, out var value) && Equals(value, pair.Value)));

            return Task.FromResult(exists);
        }

        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            var row = new Dictionary<string, object?>(values, StringComparer.Ordinal)
            {
                [descriptor.IdField] = values.TryGetValue(descriptor.IdField, out var id)
                    ? id
                    : $"{descriptor.ModelName}_{Guid.NewGuid():N}"
            };
            _rows.Add(row);
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(Project(row, descriptor.FieldSet.DetailFields));
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
            if (row is null)
            {
                return Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);
            }

            foreach (var pair in values)
            {
                row[pair.Key] = pair.Value;
            }

            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(Project(row, descriptor.FieldSet.DetailFields));
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
            Task.FromResult(_rows.RemoveAll(row => string.Equals(row[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal)) > 0);

        private static IReadOnlyDictionary<string, object?> Project(
            IReadOnlyDictionary<string, object?> row,
            IReadOnlyList<string> fields)
        {
            var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                projected[field] = row.TryGetValue(field, out var value) ? value : null;
            }

            return projected;
        }
    }

    private sealed class RecordingPlatformQueryExecutor(PlatformQueryResult result) : IPlatformQueryExecutor
    {
        public PlatformQueryRequest? LastRequest { get; private set; }

        public Task<PlatformQueryResult> QueryAsync(
            ICP.Platform.Query.Descriptors.PlatformQueryDescriptor descriptor,
            PlatformQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }
}
