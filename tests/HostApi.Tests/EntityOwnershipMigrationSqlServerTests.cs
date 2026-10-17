using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

/// <summary>冻结旧发行物经正式部署升级同一 catalog，不用删历史或人工补造旧 schema 模拟迁移。</summary>
public sealed class EntityOwnershipMigrationSqlServerTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_OWNERSHIP_UPGRADE", "正式旧三模块前缀保留对象、Link、审计及历史，经当前发行物升级补齐后重启幂等")]
    public async Task PublishedPrefix_UpgradesExistingObjectsAndLinks_BackfillPreservesAuditAndRestartIsIdempotent()
    {
        var baselineHost = RequiredPath("NEWP_OWNERSHIP_UPGRADE_BASELINE_HOST", directory: true);
        var baselinePackage = RequiredPath("NEWP_OWNERSHIP_UPGRADE_BASELINE_PACKAGE", directory: false);
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(RequiredPath("NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT", directory: false)));
        var currentHost = ExistingPath(input.RootElement.GetProperty("hostRoot").GetString()!, directory: true);
        var currentPackage = ExistingPath(input.RootElement.GetProperty("packagePath").GetString()!, directory: false);
        Assert.NotEqual(Path.GetFullPath(baselineHost), Path.GetFullPath(currentHost));
        Assert.True(File.Exists(Path.Combine(baselineHost, "HostApi.dll")));
        Assert.True(File.Exists(Path.Combine(currentHost, "HostApi.dll")));
        var versionDecisions = ReadUpgradeDecisions(baselinePackage, currentPackage);

        var timer = Stopwatch.StartNew();
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async owned =>
        {
            await DeployAsync(owned, "install-native", baselinePackage);
            await DeployAsync(owned, "enable-bundle", baselinePackage);
            var mounted = await MountBaselineMetadataAsync(owned, baselinePackage);
            output.WriteLine("baseline ancestor metadata mounted with identical byte digests files={0}, storageMap=1", mounted);
        }, sourceHostRoot: baselineHost);
        output.WriteLine("baseline formal deployment/start/bootstrap elapsedMs={0:F2}", timer.Elapsed.TotalMilliseconds);

        var root = await PostAsync(fixture, fixture.Administrator, "workspace.spaces.access", "/api/workspace/spaces/public/initialize");
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var position = new { spaceId = root.GetProperty("spaceId").GetString()!, rootFolderId = rootId, linkIds = Array.Empty<string>() };
        var suffix = Guid.NewGuid().ToString("N");
        var container = await PostAsync(fixture, fixture.Administrator, "workspace.folder.create", "/api/workspace/content/folders",
            new { position, values = new { ENAME = "升级容器" + suffix } });
        var containerId = container.GetProperty("id").GetString()!;
        var roots = await ReadContentsAsync(fixture, fixture.Administrator, position.spaceId, rootId, rootId, []);
        var containerLink = Assert.Single(roots.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("associatedObject").GetProperty("id").GetString() == containerId).GetProperty("linkId").GetString()!;
        position = position with { linkIds = new[] { containerLink } };
        var samples = new[]
        {
            new Sample("WorkspaceFolder", "folders", "workspace.folder.create", "旧目录" + suffix),
            new Sample("Part", "parts", "workspace.folder.createPart", "旧零件" + suffix),
            new Sample("Document", "documents", "workspace.folder.createDocument", "旧文档" + suffix)
        };
        foreach (var sample in samples)
        {
            object values = sample.Table == "WorkspaceFolder" ? new { ENAME = sample.Name } : new { ECODE = sample.Table + suffix, ENAME = sample.Name };
            await PostAsync(fixture, fixture.Administrator, sample.Behavior, "/api/workspace/content/" + sample.Route, new { position, values });
        }
        var contents = await ReadContentsAsync(fixture, fixture.Administrator, position.spaceId, rootId, containerId, position.linkIds);
        foreach (var sample in samples)
        {
            var item = Assert.Single(contents.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("associatedObject").GetProperty("modelKey").GetString() == sample.Table
                && item.GetProperty("fields").GetProperty("Name").GetString() == sample.Name);
            sample.Id = item.GetProperty("associatedObject").GetProperty("id").GetString()!;
            sample.LinkId = item.GetProperty("linkId").GetString()!;
        }

        // c208 已有 person.created 正式消费；旧三模型创建本身尚无该业务事件，不能把空记录当升级证据。
        var company = await PostAsync(fixture, fixture.Administrator, "organization.company.create", "/api/organization/companies",
            new { ECODE = "UPGRADE-" + suffix, ENAME = "升级审计公司" + suffix, SortOrder = "0100" });
        var companyId = company.GetProperty("fields").GetProperty("EID").GetString()!;
        var department = await PostAsync(fixture, fixture.Administrator, "organization.department.create", "/api/organization/departments",
            new { ECODE = "UPGRADE-" + suffix, ENAME = "升级审计部门" + suffix, CompanyId = companyId, SortOrder = "0100" });
        var person = await PostAsync(fixture, fixture.Administrator, "organization.person.create", "/api/organization/persons", new
        {
            PersonNo = "UPGRADE-" + suffix, ENAME = "升级审计人员" + suffix, CompanyId = companyId,
            DepartmentId = department.GetProperty("fields").GetProperty("EID").GetString()!, BirthDate = "2000-02-29",
            EmploymentStatus = "Active", EmploymentStartedAt = "2026-01-01T01:02:03Z", IsEnabled = true
        });
        var personId = person.GetProperty("fields").GetProperty("EID").GetString()!;

        var beforeObjects = new List<TableSnapshot>();
        foreach (var sample in samples)
        {
            var snapshot = await ReadSnapshotAsync(fixture, sample.Table);
            Assert.NotEmpty(snapshot.Rows);
            Assert.DoesNotContain("OwnerId", snapshot.Columns);
            Assert.DoesNotContain("OwnerDepartmentId", snapshot.Columns);
            var row = Assert.Single(snapshot.Rows, row => row.GetProperty("EID").GetString() == sample.Id);
            foreach (var audit in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" }) Assert.True(row.TryGetProperty(audit, out _));
            sample.Creator = row.GetProperty("CreatedBy").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(sample.Creator));
            beforeObjects.Add(snapshot);
        }
        var beforeLinks = await ReadSnapshotAsync(fixture, "WorkspaceFolderContent");
        Assert.Equal(4, beforeLinks.Rows.Length);
        var beforeAudit = await ReadSnapshotAsync(fixture, "BusinessChange");
        Assert.NotEmpty(beforeAudit.Rows);
        var oldRecord = Assert.Single(beforeAudit.Rows, row => row.GetProperty("EventType").GetString() == "person.created"
            && row.GetProperty("TargetId").GetString() == personId && row.GetProperty("Resource").GetString() == "organization.person");
        using (var fields = JsonDocument.Parse(oldRecord.GetProperty("FieldsJson").GetString()!))
            Assert.Equal(JsonValueKind.Array, fields.RootElement.ValueKind);
        var beforeEf = await ReadSnapshotAsync(fixture, "__EFMigrationsHistory");
        var beforeUnified = await ReadSnapshotAsync(fixture, "__NewPMigrationHistory");
        Assert.NotEmpty(beforeEf.Rows);
        Assert.NotEmpty(beforeUnified.Rows);
        AssertTail(beforeUnified, "Part", "_Part_000001_Metadata");
        AssertTail(beforeUnified, "Document", "_Document_000003_FileLink");
        AssertTail(beforeUnified, "Workspace", "_Workspace_000007_ObjectCopy");

        await fixture.StopAsync();
        RetireBaselineAuthoring(fixture);
        // 移走夹具自有旧副本，避免合并复制把旧 DLL 混入裁剪的新 Host；冻结来源从不就地修改。
        var oldCopy = Path.Combine(Path.GetDirectoryName(fixture.HostRoot)!, "baseline-host");
        var ownedRoot = Path.GetFullPath(fixture.EvidenceRoot) + Path.DirectorySeparatorChar;
        Assert.StartsWith(ownedRoot, Path.GetFullPath(fixture.HostRoot), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(ownedRoot, Path.GetFullPath(oldCopy), StringComparison.OrdinalIgnoreCase);
        Directory.Move(fixture.HostRoot, oldCopy);
        CopyTree(currentHost, fixture.HostRoot);
        timer.Restart();
        await DeployAsync(fixture, "install-native", currentPackage);
        var decisionsPath = Path.Combine(fixture.EvidenceRoot, "ownership-upgrade-version-decisions.json");
        await File.WriteAllBytesAsync(decisionsPath, JsonSerializer.SerializeToUtf8Bytes(versionDecisions, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await DeployAsync(fixture, "enable-bundle", currentPackage, decisionsPath);
        await fixture.StartAsync();
        output.WriteLine("current formal deployment/upgrade/start elapsedMs={0:F2}", timer.Elapsed.TotalMilliseconds);
        using var admin = await fixture.LoginAsync("admin", fixture.AdministratorPassword + "-ready");

        foreach (var snapshot in beforeObjects) AssertRetained(snapshot, await ReadSnapshotAsync(fixture, snapshot.Table, snapshot.Columns));
        Assert.Equal(beforeLinks.Rows, (await ReadSnapshotAsync(fixture, beforeLinks.Table, beforeLinks.Columns)).Rows, JsonRowsComparer.Instance);
        AssertRetained(beforeAudit, await ReadSnapshotAsync(fixture, beforeAudit.Table, beforeAudit.Columns));
        var upgradedEf = await ReadSnapshotAsync(fixture, beforeEf.Table, beforeEf.Columns);
        var upgradedUnified = await ReadSnapshotAsync(fixture, beforeUnified.Table, beforeUnified.Columns);
        AssertPrefix(beforeEf, upgradedEf);
        AssertPrefix(beforeUnified, upgradedUnified);
        foreach (var sample in samples)
        {
            var module = sample.Table == "WorkspaceFolder" ? "Workspace" : sample.Table;
            var migration = sample.Table switch
            {
                "WorkspaceFolder" => "20261017105016_Workspace_000008_EntityOwnership",
                "Part" => "20261017110036_Part_000002_EntityOwnership",
                _ => "20261017110042_Document_000004_EntityOwnership"
            };
            Assert.DoesNotContain(beforeEf.Rows, row => row.GetProperty("MigrationId").GetString() == migration);
            Assert.DoesNotContain(beforeUnified.Rows, row => row.GetProperty("MigrationId").GetString() == migration);
            Assert.Single(upgradedEf.Rows, row => row.GetProperty("MigrationId").GetString() == migration);
            var applied = Assert.Single(upgradedUnified.Rows, row => row.GetProperty("MigrationId").GetString() == migration);
            Assert.Equal(module, applied.GetProperty("OwnerModuleId").GetString());
            await AssertNullableOwnershipAsync(fixture, sample, expectedDepartment: null);
        }

        var organization = await PostAsync(fixture, admin, "organization.company.initialize", "/api/organization/initialization");
        var departmentId = organization.GetProperty("departmentId").GetString()!;
        var initialize = new { position, linkIds = samples.Select(sample => sample.LinkId).ToArray() };
        timer.Restart();
        var receipt = await PostAsync(fixture, admin, "workspace.folder.initializeOwnership", "/api/workspace/ownership/initialize", initialize);
        Assert.Equal(3, receipt.GetProperty("changed").GetInt32());
        Assert.Equal(0, receipt.GetProperty("unresolved").GetInt32());
        output.WriteLine("three legacy object backfill elapsedMs={0:F2}", timer.Elapsed.TotalMilliseconds);
        foreach (var sample in samples) await AssertNullableOwnershipAsync(fixture, sample, departmentId);
        foreach (var snapshot in beforeObjects) AssertRetained(snapshot, await ReadSnapshotAsync(fixture, snapshot.Table, snapshot.Columns));
        AssertRetained(beforeAudit, await ReadSnapshotAsync(fixture, beforeAudit.Table, beforeAudit.Columns));
        Assert.Equal(beforeLinks.Rows, (await ReadSnapshotAsync(fixture, beforeLinks.Table, beforeLinks.Columns)).Rows, JsonRowsComparer.Instance);
        var afterAudit = await ReadSnapshotAsync(fixture, "BusinessChange");
        var initialized = afterAudit.Rows.Where(row => row.GetProperty("EventType").GetString() == "entity.ownership.backfilled"
            && samples.Any(sample => sample.Id == row.GetProperty("TargetId").GetString())).ToArray();
        Assert.Equal(3, initialized.Length);

        await fixture.StopAsync();
        await fixture.StartAsync();
        using var restarted = await fixture.LoginAsync("admin", fixture.AdministratorPassword + "-ready");
        var retry = await PostAsync(fixture, restarted, "workspace.folder.initializeOwnership", "/api/workspace/ownership/initialize", initialize);
        Assert.Equal(0, retry.GetProperty("changed").GetInt32());
        Assert.Equal(0, retry.GetProperty("unresolved").GetInt32());
        foreach (var sample in samples) await AssertNullableOwnershipAsync(fixture, sample, departmentId);
        foreach (var snapshot in beforeObjects) AssertRetained(snapshot, await ReadSnapshotAsync(fixture, snapshot.Table, snapshot.Columns));
        Assert.Equal(afterAudit.Rows, (await ReadSnapshotAsync(fixture, "BusinessChange", afterAudit.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(beforeLinks.Rows, (await ReadSnapshotAsync(fixture, beforeLinks.Table, beforeLinks.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(upgradedEf.Rows, (await ReadSnapshotAsync(fixture, upgradedEf.Table, upgradedEf.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(upgradedUnified.Rows, (await ReadSnapshotAsync(fixture, upgradedUnified.Table, upgradedUnified.Columns)).Rows, JsonRowsComparer.Instance);
    }

    private static string RequiredPath(string variable, bool directory) => ExistingPath(Environment.GetEnvironmentVariable(variable)
        ?? throw new InvalidOperationException("启用正式升级验证后必须提供 " + variable), directory);

    // SQL 租约前只核本场景的版本前提；完整包资产仍由各自正式 install-native 严格校验。
    private static BundleVersionDecision[] ReadUpgradeDecisions(string baselinePackage, string currentPackage)
    {
        static ComponentBundleManifest ReadIndex(string package)
        {
            using var archive = ZipFile.OpenRead(package);
            var entry = Assert.Single(archive.Entries, entry => entry.FullName == "component-bundle.json");
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<ComponentBundleManifest>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("正式组件包索引为空。");
        }
        var baseline = ReadIndex(baselinePackage);
        var current = ReadIndex(currentPackage);
        foreach (var module in new[] { "Part", "Document", "Workspace" })
        {
            var oldVersion = Assert.Single(baseline.Components, member => member.ModuleId == module).Version;
            var newVersion = Assert.Single(current.Components, member => member.ModuleId == module).Version;
            Assert.True(oldVersion != newVersion, $"{module} 升级包仍复用旧版本 {oldVersion}，不能覆盖同版本库存。");
        }
        var previousVersions = baseline.Components.ToDictionary(member => member.ModuleId, member => member.Version, StringComparer.Ordinal);
        return current.Components.Where(member => previousVersions.TryGetValue(member.ModuleId, out var previous) && previous != member.Version)
            .OrderBy(member => member.ModuleId, StringComparer.Ordinal).Select(member => new BundleVersionDecision(member.ModuleId, member.Version)).ToArray();
    }

    private static string ExistingPath(string path, bool directory)
    {
        var full = Path.GetFullPath(path);
        Assert.True(directory ? Directory.Exists(full) : File.Exists(full), "正式升级输入不存在：" + full);
        return full;
    }

    private static void CopyTree(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    // 旧 provider 向上寻找 authoring；放在 Host 父目录，避免 metadata 目录被内建模块库存扫描。
    private static async Task<int> MountBaselineMetadataAsync(FeatureCatalogHostFixture fixture, string package)
    {
        string[] modules = ["Part", "Document", "Workspace"];
        using var archive = ZipFile.OpenRead(package);
        var entries = archive.Entries.Where(entry => !entry.FullName.EndsWith('/')
            && modules.Any(module => entry.FullName.StartsWith($"modules/{module}/metadata/", StringComparison.Ordinal))).ToArray();
        var ancestor = Path.GetDirectoryName(fixture.HostRoot)!;
        var root = Path.GetFullPath(ancestor) + Path.DirectorySeparatorChar;
        Assert.StartsWith(Path.GetFullPath(fixture.EvidenceRoot) + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(ancestor, "modules")));
        Assert.False(Directory.Exists(Path.Combine(ancestor, "server")));
        foreach (var entry in entries)
        {
            Assert.False(Path.IsPathRooted(entry.FullName));
            Assert.DoesNotContain('\\', entry.FullName);
            Assert.DoesNotContain(':', entry.FullName);
            Assert.All(entry.FullName.Split('/'), segment => Assert.True(segment.Length > 0 && segment is not "." and not ".."));
            var destination = Path.GetFullPath(Path.Combine(ancestor, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            Assert.StartsWith(root, destination, StringComparison.OrdinalIgnoreCase);
            await using var source = entry.Open();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, bytes);
            Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(await File.ReadAllBytesAsync(destination)));
        }
        foreach (var module in modules)
            Assert.Contains(entries, entry => entry.FullName == $"modules/{module}/metadata/models/{(module == "Workspace" ? "WorkspaceFolder" : module)}.model.json");
        const string storageMapPath = "server/platform-modeling/metadata/storage-types.json";
        var storageMap = await File.ReadAllBytesAsync(Path.Combine(fixture.HostRoot, storageMapPath));
        var storageDestination = Path.GetFullPath(Path.Combine(ancestor, storageMapPath));
        Assert.StartsWith(root, storageDestination, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(storageDestination)!);
        await File.WriteAllBytesAsync(storageDestination, storageMap);
        Assert.Equal(SHA256.HashData(storageMap), SHA256.HashData(await File.ReadAllBytesAsync(storageDestination)));
        return entries.Length;
    }

    // 当前阶段不得再命中旧定位根；只移动本夹具刚创建的两个确切目录，保留副本供失败诊断。
    private static void RetireBaselineAuthoring(FeatureCatalogHostFixture fixture)
    {
        var ancestor = Path.GetDirectoryName(fixture.HostRoot)!;
        var retired = Path.Combine(ancestor, "baseline-authoring");
        var ownedRoot = Path.GetFullPath(fixture.EvidenceRoot) + Path.DirectorySeparatorChar;
        Assert.StartsWith(ownedRoot, Path.GetFullPath(retired), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(retired));
        Directory.CreateDirectory(retired);
        foreach (var name in new[] { "modules", "server" })
        {
            var source = Path.GetFullPath(Path.Combine(ancestor, name));
            var destination = Path.GetFullPath(Path.Combine(retired, name));
            Assert.StartsWith(ownedRoot, source, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(ownedRoot, destination, StringComparison.OrdinalIgnoreCase);
            Directory.Move(source, destination);
            Assert.False(Directory.Exists(source));
        }
    }

    private static async Task DeployAsync(FeatureCatalogHostFixture fixture, string operation, string package, string? decisionsPath = null)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = fixture.HostRoot, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(fixture.HostRoot, "HostApi.dll"), "plugin-deploy", operation, "--package", package }) start.ArgumentList.Add(argument);
        if (decisionsPath is not null) { start.ArgumentList.Add("--decisions"); start.ArgumentList.Add(decisionsPath); }
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__SqlServer"] = fixture.ConnectionString;
        start.Environment["Platform__PluginInstallRoot"] = fixture.PluginRoot;
        start.Environment["Platform__ExternalPluginsEnabled"] = "false";
        start.Environment["Files__StorageRoot"] = fixture.StorageRoot;
        start.Environment["Authentication__Host__KeyRingPath"] = Path.Combine(Path.GetDirectoryName(fixture.HostRoot)!, "keys");
        start.Environment["Identity__Credentials__UseSharedDevelopmentCredential"] = "false";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动正式 plugin-deploy。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var prefix = operation + "-" + Guid.NewGuid().ToString("N");
            string Sanitize(string text) => text.Replace(fixture.ConnectionString, "[connection]", StringComparison.Ordinal)
                .Replace(fixture.AdministratorPassword, "[credential]", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, prefix + ".stdout.log"), Sanitize(await stdout));
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, prefix + ".stderr.log"), Sanitize(await stderr));
        }
    }

    private static async Task<JsonElement> PostAsync(FeatureCatalogHostFixture fixture, HttpClient client, string behavior, string path, object? input = null)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await FeatureHttpTestRequests.SendAsync(client, fixture.Catalog.First(point => point.BehaviorKey == behavior), HttpMethod.Post, path, input);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<JsonElement> ReadContentsAsync(FeatureCatalogHostFixture fixture, HttpClient client, string spaceId, string rootId, string folderId, string[] linkIds)
    {
        var scope = JsonSerializer.Serialize(new { spaceId, rootFolderId = rootId, linkIds = JsonSerializer.Serialize(linkIds) });
        var path = "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + Uri.EscapeDataString(folderId)
            + "&queryScope=" + Uri.EscapeDataString(scope) + "&pageSize=20";
        return FeatureHttpTestRequests.ReadAsync(client, fixture.Catalog.First(point => point.BehaviorKey == "workspace.spaces.access"), path);
    }

    // 从旧 catalog 取列清单，升级后按同一清单比较全部原字段，新增列不能掩盖旧字段改写。
    private static async Task<TableSnapshot> ReadSnapshotAsync(FeatureCatalogHostFixture fixture, string table, string[]? columns = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        if (columns is null)
        {
            command.CommandText = "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(@table) ORDER BY column_id;";
            command.Parameters.AddWithValue("@table", "dbo." + table);
            var names = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            columns = names.ToArray();
            Assert.NotEmpty(columns);
            command.Parameters.Clear();
        }
        var key = table == "__NewPMigrationHistory" ? "Position" : table == "__EFMigrationsHistory" ? "MigrationId" : table == "WorkspaceFolderContent" ? "LID" : "EID";
        command.CommandText = "SELECT " + string.Join(",", columns.Select(Quote)) + " FROM dbo." + Quote(table) + " ORDER BY " + Quote(key) + " FOR JSON PATH, INCLUDE_NULL_VALUES;";
        var json = "";
        await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) json += reader.GetString(0);
        return new(table, columns, JsonSerializer.Deserialize<JsonElement[]>(json.Length == 0 ? "[]" : json)!);
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    private static void AssertRetained(TableSnapshot before, TableSnapshot after) =>
        Assert.Subset(after.Rows.Select(row => row.GetRawText()).ToHashSet(StringComparer.Ordinal), before.Rows.Select(row => row.GetRawText()).ToHashSet(StringComparer.Ordinal));
    private static void AssertPrefix(TableSnapshot before, TableSnapshot after) => Assert.Equal(before.Rows, after.Rows.Take(before.Rows.Length), JsonRowsComparer.Instance);

    private static void AssertTail(TableSnapshot history, string module, string suffix) =>
        Assert.EndsWith(suffix, history.Rows.Last(row => row.GetProperty("OwnerModuleId").GetString() == module).GetProperty("MigrationId").GetString());

    private static async Task AssertNullableOwnershipAsync(FeatureCatalogHostFixture fixture, Sample sample, string? expectedDepartment)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(@table) AND name IN(N'OwnerId',N'OwnerDepartmentId') AND is_nullable=1;";
        command.Parameters.AddWithValue("@table", "dbo." + sample.Table);
        Assert.Equal(2, Convert.ToInt32(await command.ExecuteScalarAsync()));
        command.Parameters.Clear();
        command.CommandText = "SELECT OwnerId,OwnerDepartmentId FROM dbo." + Quote(sample.Table) + " WHERE EID=@id;";
        command.Parameters.AddWithValue("@id", sample.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedDepartment is null ? null : sample.Creator, reader.IsDBNull(0) ? null : reader.GetString(0));
        Assert.Equal(expectedDepartment, reader.IsDBNull(1) ? null : reader.GetString(1));
        Assert.False(await reader.ReadAsync());
    }

    private sealed record TableSnapshot(string Table, string[] Columns, JsonElement[] Rows);
    private sealed class Sample(string table, string route, string behavior, string name)
    {
        internal string Table { get; } = table;
        internal string Route { get; } = route;
        internal string Behavior { get; } = behavior;
        internal string Name { get; } = name;
        internal string Id { get; set; } = "";
        internal string LinkId { get; set; } = "";
        internal string Creator { get; set; } = "";
    }
    private sealed class JsonRowsComparer : IEqualityComparer<JsonElement>
    {
        internal static JsonRowsComparer Instance { get; } = new();
        public bool Equals(JsonElement x, JsonElement y) => x.GetRawText() == y.GetRawText();
        public int GetHashCode(JsonElement value) => StringComparer.Ordinal.GetHashCode(value.GetRawText());
    }
}
