using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DocumentFilesSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较单主文件与主文件加两附件的真实列表和下载成本")]
    public async Task FilesDownload_RecordsOneAndThreeFileCosts()
    {
        var capture = new FileReadCostCapture(); var connections = new FileCostConnections();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.Remove(Assert.Single(services, item => item.ServiceType == typeof(IHostedService) && item.ImplementationType == typeof(FileCleanupWorker)));
            services.Configure<FilesOptions>(options => options.MaxReadBatchSize = 2);
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(capture, connections));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var read = Point(fixture, "document.document.read");
        var download = Point(fixture, "document.document.download");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "FileCost313", "文件下载成本",
            "file-cost313", "下载成本普通主体", read, download);
        using var ordinary = actor.Client;
        var documents = new List<(int Count, string DocumentId, string MainLinkId)>();
        foreach (var count in new[] { 1, 3 })
        {
            var values = new { ECODE = "FILE-COST-313-" + count, ENAME = "文件下载样本" };
            var uploads = new List<string>();
            for (var index = 0; index < count; index++)
            {
                var staged = await StageAsync(fixture, admin, position, values, [0x25, 0x50, 0x44, 0x46, (byte)index], $"cost-{index}.pdf");
                uploads.Add(staged.GetProperty("uploadId").GetString()!);
            }
            using var created = await CreateDocumentAsync(fixture, admin, new
            {
                position, values, uploads = new { mainUploadId = uploads[0], attachmentUploadIds = uploads.Skip(1).ToArray() }
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var identity = await ReadCreatedAsync(fixture, values.ECODE);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var main = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().SingleAsync(row =>
                EF.Property<string>(row, "PEID") == identity.IterationId && EF.Property<long>(row, "Ordinal") == 0L);
            documents.Add((count, identity.DocumentId, (string)main["LID"]!));
        }
        var stage = Environment.GetEnvironmentVariable("NEWP_FILES_COST_STAGE") ?? "candidate";
        var baseline = stage == "baseline";
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var run = Path.Combine(CostRoot(), ".codex-temp", "issue-305", "313", "cost", stage + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        string connectionString;
        using (var scope = fixture.Services.CreateScope()) connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        foreach (var document in documents)
        {
            foreach (var content in new[] { false, true })
            {
                var feature = content ? download : read;
                var binding = baseline ? null : Assert.Single(plan.ActionDataAccessCatalog, item =>
                    (content ? item.BusinessBinding == "document.document.download" : item.Execution == "document.document.read" && item.BusinessBinding is null) && item.FeaturePositions.Contains(feature.Position));
                var path = $"/api/document/documents/{document.DocumentId}/files" + (content ? $"/{document.MainLinkId}/content" : "");
                // 暖请求只完成当前actor/功能装载；准备、暖请求和诊断重放不计入正式请求。
                using (var warm = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path, dataAccess: binding))
                    Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
                capture.Commands.Clear(); capture.Reads.Clear(); capture.Active = true; connections.Items.Clear(); connections.Active = true;
                var watch = Stopwatch.StartNew(); byte[] bytes;
                try
                {
                    using var response = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path, dataAccess: binding);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    bytes = await response.Content.ReadAsByteArrayAsync();
                }
                finally { watch.Stop(); capture.Active = false; connections.Active = false; }
                if (content) Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46, 0 }, bytes);
                else
                {
                    using var json = JsonDocument.Parse(bytes);
                    Assert.Equal(document.Count, json.RootElement.GetProperty("files").GetArrayLength());
                    Assert.True(json.RootElement.GetProperty("downloadAllowed").GetBoolean());
                }
                var reads = new List<ExampleExecutionSqlPlanTests.Measurement>();
                foreach (var sample in capture.Reads) reads.Add(await ExampleExecutionSqlPlanTests.ExplainAsync(connectionString, sample, requireIo: true));
                var operation = $"files{document.Count}-" + (content ? "download-main" : "metadata");
                await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(new
                {
                    stage, operation, fileCount = document.Count, maxReadBatchSize = 2, responseBytes = bytes.Length,
                    sqlCommands = capture.Commands.Count, commands = capture.Commands, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                    connections = connections.Items.ToArray(), readPlans = reads
                }, new JsonSerializerOptions { WriteIndented = true }));
                output.WriteLine($"{stage}/{operation}: SQL={capture.Commands.Count}, readPlans={reads.Count}, bytes={bytes.Length}, artifacts={run}");
            }
        }
    }

    private static string CostRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        return root?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // 单独记录EF连接开关及耗时，不把SQL诊断重放计入请求连接成本。
    private sealed class FileCostConnections : DbConnectionInterceptor
    {
        internal bool Active;
        internal List<object> Items { get; } = [];
        private void Record(ConnectionEndEventData data, string operation)
        {
            if (Active) Items.Add(new { data.ConnectionId, operation, elapsedMilliseconds = data.Duration.TotalMilliseconds });
        }
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData data) => Record(data, "open");
        public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData data) => Record(data, "close");
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken token = default)
        { Record(data, "open"); return Task.CompletedTask; }
        public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData data)
        { Record(data, "close"); return Task.CompletedTask; }
    }
    private sealed class FileReadCostCapture : DbCommandInterceptor
    {
        internal bool Active;
        internal List<string> Commands { get; } = [];
        internal List<ExampleExecutionSqlPlanTests.Sample> Reads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Active)
            {
                Commands.Add(command.CommandText);
                if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                    Reads.Add(new(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<object> result, CancellationToken token = default)
        {
            if (Active) Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
