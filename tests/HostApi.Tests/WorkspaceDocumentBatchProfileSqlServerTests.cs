using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "有界真实合同剖面区分来源/候选/文件准备、存储、SQL与CPU；不以延长调用截止掩盖性能")]
    public async Task ObjectCopy_DocumentBatchProfilesTheRealPreparationSeams()
    {
        var profile = new CopyPhaseProfile(); var clock = new BatchLockClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => {
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(profile, clock, new BatchTransactionClock(clock), new CopyConnectionProfile(profile)));
            services.AddScoped<IDocumentCopies>(provider => new ProfiledDocumentCopies(provider.GetRequiredService<DocumentFiles>(), profile));
            services.AddScoped<IFileCopyService>(provider => new ProfiledFileCopies((IFileCopyService)provider.GetRequiredService<IFileLifecycleService>(), profile));
            services.AddSingleton<IFileContentStore>(provider => new ProfiledCopyStorage(provider.GetRequiredService<LocalStorageProvider>(), profile));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "剖面来源"); var target = await Folder(fixture, admin, root, "剖面目标");
        string[] links = [await CreateBatchDocument(fixture, admin, source, 1), await CreateBatchDocument(fixture, admin, source, 2)];
        var prepared = await PrepareDocumentBatch(fixture, admin, source, target, links);
        using var process = Process.GetCurrentProcess(); process.Refresh(); var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        profile.Active = true; clock.Begin();
        try { await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", new { operationId = prepared.GetProperty("operationId").GetString(), values = BatchValues(links, "PROFILE") }); }
        finally
        {
            watch.Stop(); process.Refresh(); profile.Active = false; clock.Active = false;
            var evidence = new { elapsedMs = watch.Elapsed.TotalMilliseconds, cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, documents = 2, files = 4, bytes = 16,
                calls = profile.Calls.ToArray(), sql = profile.Sql.ToArray(), connections = profile.Connections.ToArray(), transactions = clock.Snapshot(), notes = "Actual owner delegates, SQL and Local store; no memory/disk sampling or SqlClient statistics; no timeout override." };
            var path = Environment.GetEnvironmentVariable("NEWP_COPY_PROFILE_ARTIFACT") ?? Path.Combine(Path.GetTempPath(), "newp-copy-profile-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true })); output.WriteLine("DOCUMENT_COPY_PROFILE " + path);
        }
    }
    private sealed class CopyPhaseProfile : DbCommandInterceptor
    {
        internal volatile bool Active; private readonly AsyncLocal<string?> phase = new();
        internal string CurrentPhase => phase.Value ?? "root"; internal ConcurrentQueue<object> Connections { get; } = new();
        internal ConcurrentQueue<object> Calls { get; } = new(); internal ConcurrentQueue<object> Sql { get; } = new();
        internal async Task<T> Measure<T>(string name, Func<Task<T>> action)
        {
            if (!Active) return await action(); var previous = phase.Value; phase.Value = name; var clock = Stopwatch.StartNew();
            try { return await action(); } finally { Calls.Enqueue(new { name, elapsedMs = clock.Elapsed.TotalMilliseconds }); phase.Value = previous; }
        }
        internal Task Measure(string name, Func<Task> action) => Measure(name, async () => { await action(); return true; });
        private void Record(CommandExecutedEventData data) { if (Active) Sql.Enqueue(new { phase = phase.Value ?? "root", elapsedMs = data.Duration.TotalMilliseconds }); }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default) { Record(data); return ValueTask.FromResult(result); }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken token = default) { Record(data); return ValueTask.FromResult(result); }
        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData data, object? result, CancellationToken token = default) { Record(data); return ValueTask.FromResult(result); }
    }
    private sealed class ProfiledDocumentCopies(IDocumentCopies inner, CopyPhaseProfile profile) : IDocumentCopies
    {
        public Task<StandardCrudError?> ValidateCopyCandidateAsync(IReadOnlyDictionary<string, object?> values, CancellationToken token = default) => profile.Measure("Document.Validate", () => inner.ValidateCopyCandidateAsync(values, token));
        public Task<DocumentCopySource> ReadCopySourceAsync(string id, CancellationToken token = default) => profile.Measure("Document.ReadSource", () => inner.ReadCopySourceAsync(id, token));
        public Task PrepareCopyFilesAsync(string confirmation, string key, CancellationToken token = default) => profile.Measure("Document.PrepareFiles", () => inner.PrepareCopyFilesAsync(confirmation, key, token));
        public Task CancelCopyFilesAsync(string confirmation, string key, CancellationToken token = default) => profile.Measure("Document.CancelFiles", () => inner.CancelCopyFilesAsync(confirmation, key, token));
    }
    private sealed class ProfiledFileCopies(IFileCopyService inner, CopyPhaseProfile profile) : IFileCopyService
    {
        public Task<FileCopyPreparation> PrepareCopyAsync(string key, FileUsageReference source, string owner, FileOwnerOperationContext context, CancellationToken token = default) => profile.Measure("Files.Prepare", () => inner.PrepareCopyAsync(key, source, owner, context, token));
        public Task<FileCopyPreparation?> ReadCopyAsync(string key, string owner, CancellationToken token = default) => profile.Measure("Files.ReadPreparation", () => inner.ReadCopyAsync(key, owner, token));
        public Task<ConfirmedFileUsage> ConfirmCopyAsync(string key, FileOwnerReference owner, FileOwnerOperationContext source, FileOwnerOperationContext creation, CancellationToken token = default) => profile.Measure("Files.Confirm", () => inner.ConfirmCopyAsync(key, owner, source, creation, token));
        public Task CancelCopyAsync(string key, string owner, CancellationToken token = default) => profile.Measure("Files.Cancel", () => inner.CancelCopyAsync(key, owner, token));
    }
    private sealed class ProfiledCopyStorage(IFileContentStore inner, CopyPhaseProfile profile) : IFileContentStore
    {
        public Task<FileContentMeasurement> WritePartialAsync(string key, Stream source, long maximum, CancellationToken token) => profile.Measure("Storage.ReadWriteHash", () => inner.WritePartialAsync(key, source, maximum, token));
        public Task CompleteAsync(string key, CancellationToken token) => profile.Measure("Storage.Complete", () => inner.CompleteAsync(key, token));
        public Task<Stream> OpenAsync(string key, CancellationToken token) => profile.Measure("Storage.Open", () => inner.OpenAsync(key, token));
        public Task DeleteContentAsync(string key, CancellationToken token) => profile.Measure("Storage.Delete", () => inner.DeleteContentAsync(key, token));
    }
    private sealed class CopyConnectionProfile(CopyPhaseProfile profile) : DbConnectionInterceptor
    {
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken token = default)
        {
            if (profile.Active) profile.Connections.Enqueue(new { phase = profile.CurrentPhase, elapsedMs = data.Duration.TotalMilliseconds,
                pooling = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection.ConnectionString).Pooling });
            return Task.CompletedTask;
        }
    }
}