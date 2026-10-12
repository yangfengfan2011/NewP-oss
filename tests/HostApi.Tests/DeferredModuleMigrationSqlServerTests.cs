using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DeferredModuleMigrationSqlServerTests
{
    private const string PartTail = "20261008100000_Part_000002_NameIndex";
    private const string DocumentTail = "20261009130000_Document_000002_NameIndex";

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式Host升级受原SQL全局迁移锁保护，等待期间不提交新DDL或历史")]
    public async Task DocumentUpgrade_WaitsForGlobalSqlLockBeforeAnyNewDdl()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        await PrepareUpgrade(fixture, "Document", DocumentTail);
        Select(fixture, "Document", true);
        var before = await History(fixture);
        await using var lockConnection = new SqlConnection(fixture.ConnectionString);
        await lockConnection.OpenAsync();
        await using var transaction = (SqlTransaction)await lockConnection.BeginTransactionAsync();
        await using var command = lockConnection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'NewP.PersistenceBootstrap',
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=0;
            SELECT @result;
            """;
        Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0);
        command.CommandText = "SELECT @@SPID;";
        var sessionId = Convert.ToInt32(await command.ExecuteScalarAsync());
        var startup = fixture.StartAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var waiting = false;
        while (!waiting && DateTime.UtcNow < deadline)
        {
            waiting = await Scalar(fixture, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id={sessionId} AND database_id=DB_ID() AND wait_type LIKE N'LCK_M_%';") > 0;
            if (!waiting) await Task.Delay(50);
        }
        Assert.True(waiting, "The real Host did not reach the held SQL migration lock.");
        Assert.False(startup.IsCompleted);
        Assert.Equal(before, await History(fixture));
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Document_ENAME';"));
        await transaction.CommitAsync();
        await startup;
        var after = await History(fixture);
        Assert.Equal(before, after.Take(before.Length));
        Assert.Equal(before.Length + 1, after.Length);
        Assert.Equal(DocumentTail, JsonNode.Parse(after[^1])!["MigrationId"]!.GetValue<string>());
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Document_ENAME';"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "history写入失败使正式新索引与两账本同step回滚，失败候选不提交活动状态")]
    public async Task DocumentHistoryWriteFailure_RollsBackStepDdlAndBothLedgers()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        await PrepareUpgrade(fixture, "Document", DocumentTail);
        Select(fixture, "Document", true);
        var before = await History(fixture);
        var efCount = await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;");
        var statePath = Path.Combine(fixture.PluginRoot, ".feature-gates/external-plugins-disabled/lifecycle/v2/state.json");
        var oldState = File.ReadAllBytes(statePath);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // 只在一次性fixture账本拒绝这个真实尾节点，故障发生在其索引DDL和EF记录之后。
            command.CommandText = $"""
                CREATE TRIGGER dbo.TR_DeferredRollback263 ON dbo.__NewPMigrationHistory
                AFTER INSERT AS
                BEGIN
                    IF EXISTS (SELECT 1 FROM inserted WHERE MigrationId=N'{DocumentTail}')
                        THROW 51063, 'DEFERRED_HISTORY_WRITE_FAILURE_263', 1;
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("DEFERRED_HISTORY_WRITE_FAILURE_263", fixture.LastError);
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Document_ENAME';"));
        Assert.Equal(before, await History(fixture));
        Assert.Equal(efCount, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;"));
        Assert.Equal(0, await Scalar(fixture, $"SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'{DocumentTail}';"));
        Assert.Equal(oldState, File.ReadAllBytes(statePath));

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TRIGGER dbo.TR_DeferredRollback263;";
            await command.ExecuteNonQueryAsync();
        }
        await fixture.StartAsync();
        var recovered = await History(fixture);
        Assert.Equal(before, recovered.Take(before.Length));
        Assert.Equal(before.Length + 1, recovered.Length);
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Document_ENAME';"));
        await fixture.StopAsync();
        await fixture.StartAsync();
        Assert.Equal(recovered, await History(fixture));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "owner不符在首个新业务DDL前阻断，失败候选不发布新活动版本")]
    public async Task ExistingOwnerMismatch_BlocksBeforeNewDdlAndActiveVersionPublication()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        await PrepareUpgrade(fixture, "Document", DocumentTail);
        Select(fixture, "Document", true);
        var statePath = Path.Combine(fixture.PluginRoot, ".feature-gates/external-plugins-disabled/lifecycle/v2/state.json");
        var oldState = File.ReadAllBytes(statePath);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dbo.__NewPMigrationHistory SET OwnerModuleId=N'Wrong.Owner' WHERE OwnerModuleId=N'Part';";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        var before = await History(fixture);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_MIGRATION_OWNER_MISMATCH", fixture.LastError);
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Document_ENAME';"));
        Assert.Equal(before, await History(fixture));
        Assert.Equal(oldState, File.ReadAllBytes(statePath));
        var storageStatePath = Assert.Single(Directory.GetFiles(Path.Combine(fixture.PluginRoot, "persistence"), "state.json", SearchOption.AllDirectories));
        var storage = JsonNode.Parse(File.ReadAllText(storageStatePath))!;
        Assert.NotNull(storage["pending"]);
        Assert.NotEqual(storage["pending"]!.GetValue<string>(), storage["successful"]!.GetValue<string>());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "初次停用安装的Part在Document升级后首次启用，从零执行自身前缀")]
    public async Task InitiallyDisabledPart_FirstEnablesAfterDocumentUpgrade()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], prepared =>
        {
            Select(prepared, "Workspace", false);
            Select(prepared, "Part", false);
            return Task.CompletedTask;
        });
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name=N'Part';"));
        await fixture.StopAsync();
        await PrepareUpgrade(fixture, "Part", PartTail);
        await PrepareUpgrade(fixture, "Document", DocumentTail);
        Select(fixture, "Part", false);
        Select(fixture, "Document", true);
        await fixture.StartAsync();
        var before = await History(fixture);
        Assert.Equal(DocumentTail, JsonNode.Parse(before[^1])!["MigrationId"]!.GetValue<string>());
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory WHERE OwnerModuleId=N'Part';"));
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name=N'Part';"));
        await fixture.StopAsync();
        Select(fixture, "Part", true);
        Select(fixture, "Workspace", true);
        await fixture.StartAsync();
        var after = await History(fixture);
        Assert.Equal(before, after.Take(before.Length));
        Assert.Equal(2, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory WHERE OwnerModuleId=N'Part';"));
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Part_ENAME';"));
        var runtime = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/plugins/active-module-runtime.json");
        Assert.Equal("1.1.0", Assert.Single(runtime.GetProperty("modules").EnumerateArray(), item => item.GetProperty("moduleId").GetString() == "Part").GetProperty("version").GetString());
        await fixture.StopAsync();
        await fixture.StartAsync();
        Assert.Equal(after, await History(fixture));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式Part延期迁移在Document升级后追加实际历史，并恢复Workspace原数据与引用")]
    public async Task DocumentUpgradeThenDeferredPart_PreservesCommittedFactsAndWorkspaceReferences()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        var root = await Post(fixture, "workspace.spaces.access", "/api/workspace/spaces/public/initialize", null);
        var position = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
        var part = await Post(fixture, "part.part.create", "/api/part/parts", new { ECODE = "DEFERRED", ENAME = "延期零组件" });
        var document = await Post(fixture, "document.document.create", "/api/document/documents", new { ECODE = "DEFERRED", ENAME = "先升级图文档" });
        var targets = new[] { (Model: "Part", Id: part.GetProperty("fields").GetProperty("EID").GetString()!), (Model: "Document", Id: document.GetProperty("fields").GetProperty("EID").GetString()!) };
        foreach (var target in targets)
            await Post(fixture, "workspace.folder.add", "/api/workspace/content/references", new { position, target = new { modelKey = target.Model, id = target.Id } });
        await fixture.StopAsync();
        var originalHistory = await History(fixture);
        await PrepareUpgrade(fixture, "Part", PartTail);
        await PrepareUpgrade(fixture, "Document", DocumentTail);
        Select(fixture, "Workspace", false);
        Select(fixture, "Part", false);
        Select(fixture, "Document", true);
        await fixture.StartAsync();
        var documentHistory = await History(fixture);
        Assert.Equal(originalHistory, documentHistory.Take(originalHistory.Length));
        Assert.Equal(DocumentTail, JsonNode.Parse(documentHistory[^1])!["MigrationId"]!.GetValue<string>());
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Part_ENAME';"));
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Document_ENAME';"));
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId is "Part" or "Workspace");
        await fixture.StopAsync();
        Select(fixture, "Part", true);
        await fixture.StartAsync();
        var partHistory = await History(fixture);
        Assert.Equal(documentHistory, partHistory.Take(documentHistory.Length));
        Assert.Equal(PartTail, JsonNode.Parse(partHistory[^1])!["MigrationId"]!.GetValue<string>());
        Assert.True(StringComparer.Ordinal.Compare(PartTail, DocumentTail) < 0);
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Part_ENAME';"));
        await fixture.StopAsync();
        Select(fixture, "Workspace", true);
        await fixture.StartAsync();
        Assert.Equal(partHistory, await History(fixture));
        foreach (var target in targets)
        {
            var read = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator, Point(fixture, target.Model.ToLowerInvariant() + "." + target.Model.ToLowerInvariant() + ".read"),
                "/api/" + target.Model.ToLowerInvariant() + "/" + target.Model.ToLowerInvariant() + "s/" + target.Id);
            Assert.Equal(target.Model == "Part" ? "延期零组件" : "先升级图文档", read.GetProperty("fields").GetProperty("ENAME").GetString());
        }
        var scope = JsonSerializer.Serialize(new { position.spaceId, position.rootFolderId, linkIds = "[]" });
        var contents = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator, Point(fixture, "workspace.spaces.access"),
            "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + position.rootFolderId + "&queryScope=" + Uri.EscapeDataString(scope));
        Assert.Equal(targets.OrderBy(target => target.Model), contents.GetProperty("items").EnumerateArray().Select(item =>
            (Model: item.GetProperty("associatedObject").GetProperty("modelKey").GetString()!, Id: item.GetProperty("associatedObject").GetProperty("id").GetString()!)).OrderBy(target => target.Model));
        await fixture.StopAsync();
        await fixture.StartAsync();
        Assert.Equal(partHistory, await History(fixture));
    }

    // 在真实模块的隔离authoring副本追加已有字段索引；不改正式历史或增加业务字段。
    internal static async Task PrepareUpgrade(FeatureCatalogHostFixture fixture, string module, string migrationId)
    {
        // 副本随既有fixture workspace归还，持久证据只保留脱敏工具日志与资产摘要。
        var root = Path.Combine(Path.GetDirectoryName(fixture.HostRoot)!, "authoring", module);
        // 新隔离源码根按 authoring 契约创建，不用读取器的 runtime 回退决定写入位置。
        var moduleRoot = Path.Combine(root, "frontend", "modules", ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ToSlug(module));
        CopyDirectory(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(fixture.RepositoryRoot, module), moduleRoot);
        var projectRoot = Path.Combine(root, "server", "modules", module, "Migrations.SqlServer");
        CopyDirectory(Path.Combine(fixture.RepositoryRoot, "server", "modules", module, "Migrations.SqlServer", "Migrations"), Path.Combine(projectRoot, "Migrations"));
        var projectName = module + ".SqlServerMigrations.csproj";
        var projectPath = Path.Combine(projectRoot, projectName);
        var project = XDocument.Load(Path.Combine(fixture.RepositoryRoot, "server", "modules", module, "Migrations.SqlServer", projectName));
        project.Save(projectPath);
        var className = module + "_000002_NameIndex";
        CopyFile(Path.Combine(fixture.RepositoryRoot, "server", "modules", module, "Migrations.SqlServer", module + "DesignTimeDbContextFactory.cs"), Path.Combine(projectRoot, module + "DesignTimeDbContextFactory.cs"));
        var modelPath = Path.Combine(moduleRoot, "metadata", "models", module + ".model.json");
        var model = JsonNode.Parse(File.ReadAllText(modelPath))!;
        model[module]!["indexes"]!.AsArray().Add(new JsonObject { ["name"] = "IX_" + module + "_ENAME", ["fields"] = new JsonArray("ENAME"), ["unique"] = false });
        File.WriteAllText(modelPath, model.ToJsonString());
        var capsulePath = Path.Combine(moduleRoot, "newp.plugin.json");
        var capsule = JsonNode.Parse(File.ReadAllText(capsulePath))!;
        capsule["version"] = "1.1.0";
        File.WriteAllText(capsulePath, capsule.ToJsonString());
        var productionModule = Path.Combine(fixture.RepositoryRoot, "server", "modules", module);
        var isolatedModule = Path.GetDirectoryName(projectRoot)!;
        foreach (var path in Directory.EnumerateFiles(productionModule, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(productionModule, path);
            if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj" or "Migrations.SqlServer")) continue;
            CopyFile(path, Path.Combine(isolatedModule, relative));
        }
        var moduleProject = XDocument.Load(Path.Combine(productionModule, module + "Module.csproj"));
        foreach (var reference in moduleProject.Descendants("ProjectReference"))
            reference.SetAttributeValue("Include", Path.GetFullPath(Path.Combine(productionModule, reference.Attribute("Include")!.Value)));
        moduleProject.Save(Path.Combine(isolatedModule, module + "Module.csproj"));
        // 隔离工程复用正式身份映射；基础依赖仍引用仓库，Capsule 只从隔离源码根嵌入。
        CopyFile(Path.Combine(fixture.RepositoryRoot, "Directory.Build.targets"), Path.Combine(root, "Directory.Build.targets"));
        var productionTool = Path.Combine(fixture.RepositoryRoot, "server/tools/component-source-build");
        var isolatedTool = Path.Combine(root, "server/tools/component-source-build");
        foreach (var path in Directory.EnumerateFiles(productionTool, "*.cs"))
            CopyFile(path, Path.Combine(isolatedTool, Path.GetFileName(path)));
        var toolProject = XDocument.Load(Path.Combine(productionTool, "ComponentSource.Build.csproj"));
        foreach (var reference in toolProject.Descendants("ProjectReference"))
            reference.SetAttributeValue("Include", Path.GetFullPath(Path.Combine(productionTool, reference.Attribute("Include")!.Value)));
        toolProject.Save(Path.Combine(isolatedTool, "ComponentSource.Build.csproj"));
        CopyFile(Path.Combine(fixture.RepositoryRoot, "server/platform-modeling/metadata/storage-types.json"), Path.Combine(root, "server/platform-modeling/metadata/storage-types.json"));
        CopyFile(Path.Combine(fixture.RepositoryRoot, "apps/host-api/HostApi.csproj"), Path.Combine(root, "apps/host-api/HostApi.csproj"));
        var hostModule = Path.Combine(root, "apps/host-api/bin/Release/net10.0/modules", module);
        CopyDirectory(fixture.HostRoot, Path.Combine(root, "apps/host-api/bin/Release/net10.0"));
        await Run(fixture, module + "-design-build", fixture.RepositoryRoot, "build", projectPath, "-c", "Release", "--disable-build-servers", "--nologo", "-v", "quiet");
        await Run(fixture, module + "-scaffold", root, "ef", "migrations", "add", className, "--project", projectPath, "--startup-project", projectPath, "--configuration", "Release", "--no-build");
        var generated = Directory.GetFiles(Path.Combine(projectRoot, "Migrations"), "*_" + className + ".cs").Single();
        var generatedId = Path.GetFileNameWithoutExtension(generated);
        // 固定本场景两个尾节点的ID，保持 Part 字典序早于 Document；源与 designer 同步，旧前缀不动。
        foreach (var suffix in new[] { ".cs", ".Designer.cs" })
        {
            var path = Path.Combine(projectRoot, "Migrations", generatedId + suffix);
            File.WriteAllText(path, File.ReadAllText(path).Replace(generatedId, migrationId, StringComparison.Ordinal));
            File.Move(path, Path.Combine(projectRoot, "Migrations", migrationId + suffix));
        }
        var migrationSource = File.ReadAllText(Path.Combine(projectRoot, "Migrations", migrationId + ".cs"));
        Assert.Contains("CreateIndex", migrationSource);
        Assert.DoesNotContain("CreateTable", migrationSource);
        Assert.DoesNotContain("DropTable", migrationSource);
        Assert.Contains("IX_" + module + "_ENAME", File.ReadAllText(Path.Combine(projectRoot, "Migrations", "EfPlatformDbContextModelSnapshot.cs")));
        await Run(fixture, module + "-build", fixture.RepositoryRoot, "build", projectPath, "-c", "Release", "--no-restore", "--disable-build-servers", "--nologo", "-v", "quiet");
        var moduleAssembly = Path.Combine(isolatedModule, "bin/Release/net10.0", module + "Module.dll");
        // 从字节装载避免锁住夹具产物；正式消费前拒绝遗漏映射或误用旧版本 DLL。
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext(module + "-isolated-capsule", isCollectible: true);
        try
        {
            using var assemblyBytes = new MemoryStream(File.ReadAllBytes(moduleAssembly));
            var assembly = loadContext.LoadFromStream(assemblyBytes);
            var resource = Assert.Single(assembly.GetManifestResourceNames(), name => name.EndsWith(".newp.plugin.json", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var embedded = JsonNode.Parse(stream)!;
            Assert.Equal(module, embedded["moduleId"]!.GetValue<string>());
            Assert.Equal("1.1.0", embedded["version"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(capsule, embedded), "Isolated Release DLL must embed its own authoring Capsule.");
        }
        finally { loadContext.Unload(); }
        var tool = Path.Combine(fixture.RepositoryRoot, "server/tools/migration-manifest/bin/Release/net10.0/MigrationManifest.dll");
        var finalize = new[] { tool, "finalize", "--module", module, "--provider", "SqlServer", "--migration", migrationId, "--compatibility", "safe-expand", "--repository-root", root };
        Assert.Contains("finalized", await Run(fixture, module + "-finalize-first", root, finalize));
        var manifestPath = Path.Combine(moduleRoot, "migrations", module + ".SqlServerMigrations.json");
        var finalized = File.ReadAllBytes(manifestPath);
        Assert.Contains("already finalized", await Run(fixture, module + "-finalize-second", root, finalize));
        Assert.Equal(finalized, File.ReadAllBytes(manifestPath));
        CopyDirectory(moduleRoot, hostModule);
        File.Copy(capsulePath, Path.Combine(hostModule, "plugin.json"), true);
        var bundle = Path.Combine(projectRoot, "bin/Release/net10.0", module + ".SqlServerMigrations.dll");
        CopyFile(bundle, Path.Combine(hostModule, "migrations", module + ".SqlServerMigrations.dll"));
        CopyFile(moduleAssembly, Path.Combine(root, "apps/host-api/bin/Release/net10.0", module + "Module.dll"));
        await Run(fixture, module + "-check", root, tool, "check", "--all", "--no-build", "--repository-root", root);
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, module + "-authoring.json"), JsonSerializer.Serialize(new
        { module, provider = "SqlServer", migrationId, compatibility = "safe-expand", source = fixture.RepositoryRoot, manifestSha256 = Convert.ToHexStringLower(SHA256.HashData(finalized)), bundleSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(bundle))) }));
        CopyDirectory(hostModule, Path.Combine(fixture.HostRoot, "modules", module));
        CopyFile(moduleAssembly, Path.Combine(fixture.HostRoot, module + "Module.dll"));
    }

    private static async Task<string> Run(FeatureCatalogHostFixture fixture, string name, string directory, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var captureCancellation = new CancellationTokenSource();
        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();
        var captures = Task.WhenAll(Capture(process.StandardOutput, output), Capture(process.StandardError, error));
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (TimeoutException)
        {
            timedOut = true;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        // 子进程已退出也不能无限等待被构建server继承的输出管道；取消读取并保存已收到的原始行。
        try { await captures.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException)
        {
            timedOut = true;
            captureCancellation.Cancel();
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            await captures.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var text = output.ToString() + error;
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, name + ".log"), text);
        Assert.False(timedOut, name + ": child process or output capture timed out. " + text);
        Assert.True(process.ExitCode == 0, name + ": " + text);
        return text;

        async Task Capture(StreamReader reader, System.Text.StringBuilder destination)
        {
            try
            {
                while (await reader.ReadLineAsync(captureCancellation.Token) is { } line) destination.AppendLine(line);
            }
            catch (Exception exception) when (captureCancellation.IsCancellationRequested
                && exception is OperationCanceledException or ObjectDisposedException or IOException) { }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            CopyFile(path, Path.Combine(destination, Path.GetRelativePath(source, path)));
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static void Select(FeatureCatalogHostFixture fixture, string module, bool enabled) => OptionalComponentLifecycleSqlServerTests.SetEnabled(fixture, module, enabled);
    private static FeaturePoint Point(FeatureCatalogHostFixture fixture, string behavior) => Assert.Single(fixture.Catalog, point => point.BehaviorKey == behavior);
    private static async Task<JsonElement> Post(FeatureCatalogHostFixture fixture, string behavior, string path, object? input)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, Point(fixture, behavior), HttpMethod.Post, path, input);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string[]> History(FeatureCatalogHostFixture fixture)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM dbo.__NewPMigrationHistory ORDER BY Position FOR JSON PATH;";
        await using var reader = await command.ExecuteReaderAsync();
        var text = "";
        while (await reader.ReadAsync()) text += reader.GetString(0);
        return JsonSerializer.Deserialize<JsonElement[]>(text)!.Select(row => row.GetRawText()).ToArray();
    }

    private static async Task<int> Scalar(FeatureCatalogHostFixture fixture, string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
