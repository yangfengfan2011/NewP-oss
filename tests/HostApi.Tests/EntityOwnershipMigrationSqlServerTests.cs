using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection.Metadata;
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

    [EnvironmentGatedFact("NEWP_RUN_OBJECT_PERMISSIONS_UPGRADE", "仅 PE 读取冻结新旧 Workspace 正式包，核 permissions 路由差异，不启动 Host 或 SQL")]
    public async Task PublishedOwnershipRelease_WorkspacePermissionRoutesDifferInFrozenImages()
    {
        var baselinePackage = RequiredPath("NEWP_OWNERSHIP_UPGRADE_BASELINE_PACKAGE", directory: false);
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(RequiredPath("NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT", directory: false)));
        var currentPackage = ExistingPath(input.RootElement.GetProperty("packagePath").GetString()!, directory: false);
        Assert.Equal("254cec7a05ead3923fafc938205818a0fa44009310c2d85e7dbcef0c638bfec5", await ReadPermissionFileHashAsync(baselinePackage));
        Assert.Equal(input.RootElement.GetProperty("packageSha256").GetString(), await ReadPermissionFileHashAsync(currentPackage));
        var oldImage = await ReadPermissionWorkspaceImageAsync(baselinePackage);
        var currentImage = await ReadPermissionWorkspaceImageAsync(currentPackage);
        AssertOldWorkspaceFourArgumentCall(oldImage);
        var oldRoutes = ReadPermissionWorkspaceHttpRoutes(oldImage);
        var currentRoutes = ReadPermissionWorkspaceHttpRoutes(currentImage);
        AssertNoPermissionWorkspaceRoutes(oldRoutes);
        // 正向核当前真实方法及 HTTP 模板，避免旧版缺失断言因检查虚构类名而恒真。
        foreach (var expected in new[]
        {
            new PermissionHttpRoute(nameof(ICP.Platform.Modules.Workspace.WorkspaceContentsController.Permissions), "HttpGetAttribute", "content/{linkId}/permissions/explanation"),
            new PermissionHttpRoute(nameof(ICP.Platform.Modules.Workspace.WorkspaceContentsController.PermissionCatalog), "HttpGetAttribute", "content/{linkId}/permissions/catalog"),
            new PermissionHttpRoute(nameof(ICP.Platform.Modules.Workspace.WorkspaceContentsController.PermissionUsers), "HttpGetAttribute", "content/{linkId}/permissions/subjects/user"),
            new PermissionHttpRoute(nameof(ICP.Platform.Modules.Workspace.WorkspaceContentsController.PermissionSubjects), "HttpGetAttribute", "content/{linkId}/permissions/subjects/{type}"),
            new PermissionHttpRoute(nameof(ICP.Platform.Modules.Workspace.WorkspaceContentsController.PermissionEligibility), "HttpGetAttribute", "content/{linkId}/permissions/eligibility"),
            new PermissionHttpRoute(nameof(ICP.Platform.Modules.Workspace.WorkspaceContentsController.PermissionConfiguration), "HttpGetAttribute", "content/{linkId}/permissions"),
            new PermissionHttpRoute(nameof(ICP.Platform.Modules.Workspace.WorkspaceContentsController.ReplacePermissions), "HttpPutAttribute", "content/{linkId}/permissions")
        })
            Assert.Contains(expected, currentRoutes);
        output.WriteLine(JsonSerializer.Serialize(new {
            baselinePackage, currentPackage,
            oldWorkspaceSha256 = Convert.ToHexStringLower(SHA256.HashData(oldImage)),
            currentWorkspaceSha256 = Convert.ToHexStringLower(SHA256.HashData(currentImage)),
            oldRoutes, currentRoutes }));
    }

    [EnvironmentGatedFact("NEWP_RUN_OBJECT_PERMISSIONS_UPGRADE", "冻结归属发行物升级直接授权期限，真实旧Workspace四参IL先在新Host消费，旧配置与历史及重启无变化保存保持")]
    public async Task PublishedOwnershipRelease_UpgradesDirectExpiry_ConsumesOldWorkspaceIlAndKeepsNoOpWritesIdempotent()
    {
        var baselineHost = RequiredPath("NEWP_OWNERSHIP_UPGRADE_BASELINE_HOST", directory: true);
        var baselinePackage = RequiredPath("NEWP_OWNERSHIP_UPGRADE_BASELINE_PACKAGE", directory: false);
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(RequiredPath("NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT", directory: false)));
        var currentHost = ExistingPath(input.RootElement.GetProperty("hostRoot").GetString()!, directory: true);
        var currentPackage = ExistingPath(input.RootElement.GetProperty("packagePath").GetString()!, directory: false);
        Assert.NotEqual(Path.GetFullPath(baselineHost), Path.GetFullPath(currentHost));
        var baselinePackageHash = await ReadPermissionFileHashAsync(baselinePackage);
        Assert.Equal("254cec7a05ead3923fafc938205818a0fa44009310c2d85e7dbcef0c638bfec5", baselinePackageHash);
        Assert.Equal(input.RootElement.GetProperty("packageSha256").GetString(),
            await ReadPermissionFileHashAsync(currentPackage));
        var decisions = ReadUpgradeDecisions(baselinePackage, currentPackage);
        var oldAuthorizationVersion = ReadAuthorizationVersion(baselineHost);
        var currentAuthorizationVersion = ReadAuthorizationVersion(currentHost);
        Assert.Equal("1.0.0", oldAuthorizationVersion);
        Assert.NotEqual(oldAuthorizationVersion, currentAuthorizationVersion);
        var currentAuthorizationHash = await ReadPermissionFileHashAsync(Path.Combine(currentHost, "AuthorizationModule.dll"));
        var oldContractsHash = await ReadPermissionFileHashAsync(Path.Combine(baselineHost, "Authorization.Contracts.dll"));
        var currentContractsHash = await ReadPermissionFileHashAsync(Path.Combine(currentHost, "Authorization.Contracts.dll"));
        Assert.NotEqual(oldContractsHash, currentContractsHash);
        byte[] oldWorkspaceImage;
        using (var archive = ZipFile.OpenRead(baselinePackage))
        {
            var index = Assert.Single(archive.Entries, entry => entry.FullName == "component-bundle.json");
            using var indexStream = index.Open();
            using var manifest = JsonDocument.Parse(indexStream);
            Assert.Equal("1.1.0", Assert.Single(manifest.RootElement.GetProperty("components").EnumerateArray(),
                item => item.GetProperty("moduleId").GetString() == "Workspace").GetProperty("version").GetString());
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "modules/Workspace/backend/Authorization.Contracts.dll");
            await using var source = Assert.Single(archive.Entries, entry => entry.FullName == "modules/Workspace/backend/WorkspaceModule.dll").Open();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            oldWorkspaceImage = buffer.ToArray();
        }
        AssertOldWorkspaceFourArgumentCall(oldWorkspaceImage);
        var oldWorkspaceHash = Convert.ToHexStringLower(SHA256.HashData(oldWorkspaceImage));
        output.WriteLine("frozen legacy WorkspaceSha256={0}, oldContractsSha256={1}, currentContractsSha256={2}", oldWorkspaceHash, oldContractsHash, currentContractsHash);
        var timer = Stopwatch.StartNew();
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async owned =>
        {
            await DeployAsync(owned, "install-native", baselinePackage);
            await DeployAsync(owned, "enable-bundle", baselinePackage);
            await MountBaselineMetadataAsync(owned, baselinePackage);
        }, sourceHostRoot: baselineHost);
        output.WriteLine("permissions baseline deployment/start elapsedMs={0:F2}, packageSha256={1}", timer.Elapsed.TotalMilliseconds, baselinePackageHash);
        await PostAsync(fixture, fixture.Administrator, "organization.company.initialize", "/api/organization/initialization");
        var root = await PostAsync(fixture, fixture.Administrator, "workspace.spaces.access", "/api/workspace/spaces/public/initialize");
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var position = new { spaceId = root.GetProperty("spaceId").GetString()!, rootFolderId = rootId, linkIds = Array.Empty<string>() };
        var suffix = Guid.NewGuid().ToString("N");
        var container = await PostAsync(fixture, fixture.Administrator, "workspace.folder.create", "/api/workspace/content/folders",
            new { position, values = new { ENAME = "期限升级容器" + suffix } });
        var containerId = container.GetProperty("id").GetString()!;
        var roots = await ReadContentsAsync(fixture, fixture.Administrator, position.spaceId, rootId, rootId, []);
        var containerLink = Assert.Single(roots.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("associatedObject").GetProperty("id").GetString() == containerId).GetProperty("linkId").GetString()!;
        position = position with { linkIds = new[] { containerLink } };
        await PostAsync(fixture, fixture.Administrator, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position, values = new { ECODE = "EXPIRY-" + suffix, ENAME = "旧期限零组件" + suffix } });
        var contents = await ReadContentsAsync(fixture, fixture.Administrator, position.spaceId, rootId, containerId, position.linkIds);
        var part = Assert.Single(contents.GetProperty("items").EnumerateArray(), item => item.GetProperty("associatedObject").GetProperty("modelKey").GetString() == "Part");
        var partId = part.GetProperty("associatedObject").GetProperty("id").GetString()!;
        var partLink = part.GetProperty("linkId").GetString()!;
        var entryId = "DataScopeSubject_" + suffix;
        var legacyEndpoint = "/api/authorization/data-scope/objects/part.part/" + Uri.EscapeDataString(partId);
        // 旧请求使用其真实六字段主体合同，不能把当前 DTO 的 null 期限发送给旧服务器。
        var legacyConfiguration = new { ResourceKey = "part.part", ObjectId = partId, Subjects = new[] {
            new { Id = entryId, Type = "all", SubjectId = (string?)null, Priority = 0L,
                Actions = new Dictionary<string, string> { ["read"] = "allow" }, MatchOptions = (object?)null } } };
        await PutPermissionAsync(fixture.Administrator, fixture.Catalog.First(point => point.BehaviorKey == "part.part.authorize"),
            legacyEndpoint, legacyConfiguration);
        var oldConfiguration = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator,
            fixture.Catalog.First(point => point.BehaviorKey == "part.part.authorize"), legacyEndpoint);
        AssertPermissionConfiguration(oldConfiguration, partId, entryId, expectedExpiry: null);
        var oldSubjects = await ReadSnapshotAsync(fixture, "DataScopeObjectSubject");
        Assert.DoesNotContain("ExpiresAt", oldSubjects.Columns);
        Assert.Single(oldSubjects.Rows, row => row.GetProperty("EID").GetString() == entryId);
        var oldActions = await ReadSnapshotAsync(fixture, "DataScopeObjectAction");
        Assert.NotEmpty(oldActions.Rows);
        var oldPart = await ReadSnapshotAsync(fixture, "Part");
        Assert.NotEmpty(oldPart.Rows);
        var oldPartRow = Assert.Single(oldPart.Rows, row => row.GetProperty("EID").GetString() == partId);
        foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt", "OwnerId", "OwnerDepartmentId" })
            Assert.Contains(field, oldPart.Columns);
        foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
            Assert.NotEqual(JsonValueKind.Null, oldPartRow.GetProperty(field).ValueKind);
        var oldFolders = await ReadSnapshotAsync(fixture, "WorkspaceFolder");
        var oldLinks = await ReadSnapshotAsync(fixture, "WorkspaceFolderContent");
        Assert.NotEmpty(oldFolders.Rows);
        Assert.Equal(2, oldLinks.Rows.Length);
        var oldAudit = await ReadSnapshotAsync(fixture, "BusinessChange");
        var legacyRecord = Assert.Single(oldAudit.Rows, row => IsPermissionRecord(row, partId));
        using (var fields = JsonDocument.Parse(legacyRecord.GetProperty("FieldsJson").GetString()!))
            Assert.NotEmpty(fields.RootElement.EnumerateArray());
        var oldEf = await ReadSnapshotAsync(fixture, "__EFMigrationsHistory");
        var oldUnified = await ReadSnapshotAsync(fixture, "__NewPMigrationHistory");
        Assert.NotEmpty(oldEf.Rows); Assert.NotEmpty(oldUnified.Rows);
        const string expiryMigration = "20261017151541_AddDataScopeObjectExpiry";
        Assert.DoesNotContain(oldEf.Rows, row => row.GetProperty("MigrationId").GetString() == expiryMigration);
        Assert.DoesNotContain(oldUnified.Rows, row => row.GetProperty("MigrationId").GetString() == expiryMigration);

        await fixture.StopAsync();
        // 保留祖先旧 metadata，供真实旧 DLL 在新 Host 内继续定位；此时不安装新组件包。
        var retiredHost = Path.Combine(Path.GetDirectoryName(fixture.HostRoot)!, "permissions-baseline-host");
        var ownedRoot = Path.GetFullPath(fixture.EvidenceRoot) + Path.DirectorySeparatorChar;
        Assert.StartsWith(ownedRoot, Path.GetFullPath(fixture.HostRoot), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(ownedRoot, Path.GetFullPath(retiredHost), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(retiredHost));
        Directory.Move(fixture.HostRoot, retiredHost);
        CopyTree(currentHost, fixture.HostRoot);
        var selectionPath = Path.Combine(fixture.PluginRoot, "lifecycle", "v2", "state.json");
        Assert.StartsWith(ownedRoot, Path.GetFullPath(selectionPath), StringComparison.OrdinalIgnoreCase);
        using (var previous = JsonDocument.Parse(await File.ReadAllBytesAsync(selectionPath)))
        {
            // Host 内建的改版显式更新单一 desired；不借 bundle 决策切换旧 native 组件。
            await SelectPermissionBuiltinAuthorizationAsync(fixture, currentAuthorizationVersion);
            using var selected = JsonDocument.Parse(await File.ReadAllBytesAsync(selectionPath));
            var before = previous.RootElement.GetProperty("desiredModules").EnumerateArray().ToArray();
            var after = selected.RootElement.GetProperty("desiredModules").EnumerateArray().ToArray();
            Assert.Equal(before.Length, after.Length);
            foreach (var choice in before)
            {
                var id = choice.GetProperty("moduleId").GetString();
                var actual = Assert.Single(after, item => item.GetProperty("moduleId").GetString() == id);
                Assert.Equal(id == "Authorization" ? currentAuthorizationVersion : choice.GetProperty("version").GetString(), actual.GetProperty("version").GetString());
                Assert.Equal(choice.GetProperty("enabled").GetBoolean(), actual.GetProperty("enabled").GetBoolean());
            }
            foreach (var moduleId in new[] { "Workspace", "Part", "Document" })
                Assert.Equal("1.1.0", Assert.Single(after, item => item.GetProperty("moduleId").GetString() == moduleId).GetProperty("version").GetString());
            foreach (var pointer in new[] { "activeSnapshotId", "lastKnownGoodSnapshotId" })
                Assert.Equal(previous.RootElement.GetProperty(pointer).GetRawText(), selected.RootElement.GetProperty(pointer).GetRawText());
        }
        Assert.Equal(oldWorkspaceHash, await ReadPermissionFileHashAsync(Path.Combine(fixture.PluginRoot, "packages", "Workspace", "1.1.0", "backend", "WorkspaceModule.dll")));
        timer.Restart();
        await fixture.StartAsync();
        using var abiAdmin = await fixture.LoginAsync("admin", fixture.AdministratorPassword + "-ready");
        var nativeRoot = Path.Combine(fixture.PluginRoot, "packages", "Workspace", "1.1.0");
        var oldImage = await AssertPermissionModuleImageAsync(abiAdmin, "Workspace", "1.1.0", nativeRoot, nativeRoot, oldWorkspaceHash);
        var authorizationImage = await AssertPermissionModuleImageAsync(abiAdmin, "Authorization", currentAuthorizationVersion,
            Path.Combine(fixture.HostRoot, "modules", "Authorization"), fixture.HostRoot, currentAuthorizationHash);
        Assert.Equal(currentContractsHash, await ReadPermissionFileHashAsync(Path.Combine(fixture.HostRoot, "Authorization.Contracts.dll")));
        var abiContents = await ReadContentsAsync(fixture, abiAdmin, position.spaceId, rootId, containerId, position.linkIds);
        Assert.Contains(abiContents.GetProperty("items").EnumerateArray(), item => item.GetProperty("linkId").GetString() == partLink);
        // 旧 DTO 的 Swagger 短名冲突与本次升级无关；直接核已选中原物理 DLL 的 HTTP 声明。
        var oldRoutes = ReadPermissionWorkspaceHttpRoutes(await File.ReadAllBytesAsync(oldImage.Path));
        AssertNoPermissionWorkspaceRoutes(oldRoutes);
        var scope = Uri.EscapeDataString(JsonSerializer.Serialize(position));
        using (var missing = await FeatureHttpTestRequests.SendAsync(abiAdmin,
            fixture.Catalog.First(point => point.ModuleId == "Workspace" && point.BehaviorKey == "workspace.spaces.access"), HttpMethod.Get,
            $"/api/workspace/content/{Uri.EscapeDataString(partLink)}/permissions/catalog?queryScope={scope}"))
            // 已认证未知路由按正式 deny-all fallback 返回 403；缺路由事实由前面的选中原 DLL HTTP 元数据独立证明。
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, missing.StatusCode);
        await File.WriteAllBytesAsync(Path.Combine(fixture.EvidenceRoot, "permissions-upgrade-abi.json"), JsonSerializer.SerializeToUtf8Bytes(new {
            baselinePackageHash, oldContractsHash, currentContractsHash, oldImage, authorizationImage,
            consumedPath = "/api/links/workspace.content", consumedLinkId = partLink }));
        output.WriteLine("current Host consumed old native Workspace IL elapsedMs={0:F2}, WorkspaceSha256={1}, AuthorizationSha256={2}",
            timer.Elapsed.TotalMilliseconds, oldWorkspaceHash, currentAuthorizationHash);
        await AssertPermissionExpiryAsync(fixture, entryId, expectedExpiry: null);
        foreach (var snapshot in new[] { oldSubjects, oldActions, oldPart, oldFolders, oldLinks, oldAudit })
            AssertRetained(snapshot, await ReadSnapshotAsync(fixture, snapshot.Table, snapshot.Columns));
        var abiEf = await ReadSnapshotAsync(fixture, oldEf.Table, oldEf.Columns);
        var abiUnified = await ReadSnapshotAsync(fixture, oldUnified.Table, oldUnified.Columns);
        AssertPrefix(oldEf, abiEf); AssertPrefix(oldUnified, abiUnified);
        Assert.Single(abiEf.Rows, row => row.GetProperty("MigrationId").GetString() == expiryMigration);
        Assert.Equal("platform.foundation", Assert.Single(abiUnified.Rows,
            row => row.GetProperty("MigrationId").GetString() == expiryMigration).GetProperty("OwnerModuleId").GetString());

        await fixture.StopAsync();
        RetireBaselineAuthoring(fixture);
        await DeployAsync(fixture, "install-native", currentPackage);
        var decisionsPath = Path.Combine(fixture.EvidenceRoot, "permissions-upgrade-version-decisions.json");
        await File.WriteAllBytesAsync(decisionsPath, JsonSerializer.SerializeToUtf8Bytes(decisions, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await DeployAsync(fixture, "enable-bundle", currentPackage, decisionsPath);
        await fixture.StartAsync();
        using var admin = await fixture.LoginAsync("admin", fixture.AdministratorPassword + "-ready");
        var manage = fixture.Catalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var read = fixture.Catalog.Single(point => point.ModuleId == "Part" && point.Key == "part.parts" && point.Kind == "navigation");
        var endpoint = "/api/part/parts/" + Uri.EscapeDataString(partId) + "/permissions";
        AssertPermissionConfiguration(await FeatureHttpTestRequests.ReadAsync(admin, manage, endpoint), partId, entryId, expectedExpiry: null);
        using (var readable = await FeatureHttpTestRequests.SendAsync(admin, read, HttpMethod.Get, "/api/part/parts/" + Uri.EscapeDataString(partId)))
            Assert.Equal(System.Net.HttpStatusCode.OK, readable.StatusCode);
        foreach (var snapshot in new[] { oldSubjects, oldActions, oldPart, oldFolders, oldLinks, oldAudit })
            AssertRetained(snapshot, await ReadSnapshotAsync(fixture, snapshot.Table, snapshot.Columns));
        var upgradedEf = await ReadSnapshotAsync(fixture, oldEf.Table, oldEf.Columns);
        var upgradedUnified = await ReadSnapshotAsync(fixture, oldUnified.Table, oldUnified.Columns);
        AssertPrefix(abiEf, upgradedEf); AssertPrefix(abiUnified, upgradedUnified);
        await PutPermissionAsync(admin, manage, endpoint, legacyConfiguration);
        await PutPermissionAsync(admin, manage, endpoint, ConfigurationWithExpiry(partId, entryId, null));
        Assert.Equal(oldAudit.Rows, (await ReadSnapshotAsync(fixture, oldAudit.Table, oldAudit.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(oldSubjects.Rows, (await ReadSnapshotAsync(fixture, oldSubjects.Table, oldSubjects.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(oldActions.Rows, (await ReadSnapshotAsync(fixture, oldActions.Table, oldActions.Columns)).Rows, JsonRowsComparer.Instance);
        await AssertPermissionExpiryAsync(fixture, entryId, expectedExpiry: null);

        const string expiry = "2099-01-02T03:04:05Z";
        await PutPermissionAsync(admin, manage, endpoint, ConfigurationWithExpiry(partId, entryId, expiry));
        await AssertPermissionExpiryAsync(fixture, entryId, expiry);
        AssertPermissionConfiguration(await FeatureHttpTestRequests.ReadAsync(admin, manage, endpoint), partId, entryId, expiry);
        var changedAudit = await ReadSnapshotAsync(fixture, "BusinessChange");
        AssertRetained(oldAudit, await ReadSnapshotAsync(fixture, oldAudit.Table, oldAudit.Columns));
        Assert.Equal(oldAudit.Rows.Length + 1, changedAudit.Rows.Length);
        var previousIds = oldAudit.Rows.Select(row => row.GetProperty("EID").GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.True(IsPermissionRecord(Assert.Single(changedAudit.Rows, row => !previousIds.Contains(row.GetProperty("EID").GetString())), partId));
        var changedSubjects = await ReadSnapshotAsync(fixture, "DataScopeObjectSubject");
        foreach (var snapshot in new[] { oldPart, oldFolders, oldLinks })
            AssertRetained(snapshot, await ReadSnapshotAsync(fixture, snapshot.Table, snapshot.Columns));
        await fixture.StopAsync();
        await fixture.StartAsync();
        using var restarted = await fixture.LoginAsync("admin", fixture.AdministratorPassword + "-ready");
        var restartedManage = fixture.Catalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        await PutPermissionAsync(restarted, restartedManage, endpoint, ConfigurationWithExpiry(partId, entryId, expiry));
        AssertPermissionConfiguration(await FeatureHttpTestRequests.ReadAsync(restarted, restartedManage, endpoint), partId, entryId, expiry);
        Assert.Equal(changedSubjects.Rows, (await ReadSnapshotAsync(fixture, changedSubjects.Table, changedSubjects.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(changedAudit.Rows, (await ReadSnapshotAsync(fixture, changedAudit.Table, changedAudit.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(upgradedEf.Rows, (await ReadSnapshotAsync(fixture, upgradedEf.Table, upgradedEf.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(upgradedUnified.Rows, (await ReadSnapshotAsync(fixture, upgradedUnified.Table, upgradedUnified.Columns)).Rows, JsonRowsComparer.Instance);
    }

    // 只读取正式 ZIP 内的既有 backend 字节，不加载业务程序集或建立另一套探针宿主。
    private static async Task<byte[]> ReadPermissionWorkspaceImageAsync(string package)
    {
        using var archive = ZipFile.OpenRead(package);
        await using var stream = Assert.Single(archive.Entries, entry => entry.FullName == "modules/Workspace/backend/WorkspaceModule.dll").Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    // partial 控制器在新旧版本中都是同一个真实类型；比较方法上的 MVC 声明，不推断类名或扫描字符串。
    private static PermissionHttpRoute[] ReadPermissionWorkspaceHttpRoutes(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var reader = pe.GetMetadataReader();
        var currentType = typeof(ICP.Platform.Modules.Workspace.WorkspaceContentsController);
        var handle = Assert.Single(reader.TypeDefinitions, handle =>
        {
            var type = reader.GetTypeDefinition(handle);
            return reader.GetString(type.Namespace) == currentType.Namespace && reader.GetString(type.Name) == currentType.Name;
        });
        var controller = reader.GetTypeDefinition(handle);
        var root = Assert.Single(controller.GetCustomAttributes().Select(ReadRouteAttribute), route => route?.Name == "RouteAttribute");
        Assert.Equal("api/workspace", root!.Value.Template);
        var routes = new List<PermissionHttpRoute>();
        foreach (var methodHandle in controller.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            foreach (var attribute in method.GetCustomAttributes())
                if (ReadRouteAttribute(attribute) is { } route && route.Name != "RouteAttribute")
                    routes.Add(new(reader.GetString(method.Name), route.Name, route.Template));
        }
        Assert.NotEmpty(routes);
        return routes.ToArray();

        (string Name, string Template)? ReadRouteAttribute(CustomAttributeHandle attributeHandle)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) return null;
            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) return null;
            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            var name = reader.GetString(type.Name);
            if (reader.GetString(type.Namespace) != "Microsoft.AspNetCore.Mvc"
                || name is not ("RouteAttribute" or "HttpGetAttribute" or "HttpPutAttribute" or "HttpPostAttribute"
                    or "HttpDeleteAttribute" or "HttpPatchAttribute" or "HttpHeadAttribute" or "HttpOptionsAttribute")) return null;
            var signature = reader.GetBlobReader(constructor.Signature);
            Assert.False(signature.ReadSignatureHeader().IsGeneric);
            var parameterCount = signature.ReadCompressedInteger();
            Assert.InRange(parameterCount, 0, 1);
            var value = reader.GetBlobReader(attribute.Value);
            Assert.Equal((ushort)1, value.ReadUInt16());
            return (name, parameterCount == 0 ? string.Empty : value.ReadSerializedString() ?? string.Empty);
        }
    }

    private static void AssertNoPermissionWorkspaceRoutes(PermissionHttpRoute[] routes)
    {
        Assert.DoesNotContain(routes, route => route.Template.StartsWith("content/{linkId}/permissions", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, route => route.Method == nameof(ICP.Platform.Modules.Workspace.WorkspaceContentsController.PermissionCatalog));
    }

    private sealed record PermissionHttpRoute(string Method, string HttpAttribute, string Template);

    // 当前发行 Host 只改选自身签发的 Authorization 版本；保持旧 native 选择及 active/LKG 到正式重启。
    private static async Task SelectPermissionBuiltinAuthorizationAsync(FeatureCatalogHostFixture fixture, string version)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = fixture.HostRoot, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(fixture.HostRoot, "HostApi.dll"), "plugin-deploy", "select", "--module-id", "Authorization", "--version", version })
            start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__SqlServer"] = fixture.ConnectionString;
        start.Environment["Platform__PluginInstallRoot"] = fixture.PluginRoot;
        start.Environment["Platform__ExternalPluginsEnabled"] = "false";
        start.Environment["Files__StorageRoot"] = fixture.StorageRoot;
        start.Environment["Authentication__Host__KeyRingPath"] = Path.Combine(Path.GetDirectoryName(fixture.HostRoot)!, "keys");
        start.Environment["Identity__Credentials__UseSharedDevelopmentCredential"] = "false";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动正式内建组件选版。");
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
            // 与既有部署记录相同，私有输出不保留数据库连接或临时密码。
            string Sanitize(string text) => text.Replace(fixture.ConnectionString, "[connection]", StringComparison.Ordinal)
                .Replace(fixture.AdministratorPassword, "[credential]", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "permissions-select-authorization.stdout.log"), Sanitize(await stdout));
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "permissions-select-authorization.stderr.log"), Sanitize(await stderr));
        }
    }

    // 固定旧入口不复用当前 DTO；此构造器仅用于新服务器的显式 null 和规范 UTC 期限请求。
    private static object ConfigurationWithExpiry(string objectId, string entryId, string? expiresAt) => new
    {
        ResourceKey = "part.part", ObjectId = objectId, Subjects = new[] { new { Id = entryId, Type = "all", SubjectId = (string?)null,
            Priority = 0L, Actions = new Dictionary<string, string> { ["read"] = "allow" }, MatchOptions = (object?)null, ExpiresAt = expiresAt } }
    };

    // 发行包按流核摘要，输入不随整包大小增加等量临时字节数组。
    private static async Task<string> ReadPermissionFileHashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }

    // 读取发行目录自身的内建版本，不把 ZIP 缺失的 Authorization 虚构为 bundle 决策成员。
    private static string ReadAuthorizationVersion(string host)
    {
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(host, "modules", "Authorization", "plugin.json")));
        return capsule.RootElement.GetProperty("version").GetString()!;
    }

    // 真实旧 ConfigureServices 方法必须含四参 newobj，单看当前源码或无调用的 MemberRef 不构成 ABI 证据。
    private static void AssertOldWorkspaceFourArgumentCall(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var reader = pe.GetMetadataReader();
        var constructor = Assert.Single(reader.MemberReferences, handle =>
        {
            var member = reader.GetMemberReference(handle);
            if (member.Parent.Kind != System.Reflection.Metadata.HandleKind.TypeReference || reader.GetString(member.Name) != ".ctor") return false;
            var type = reader.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)member.Parent);
            return reader.GetString(type.Name) == "DataScopeResource" && reader.GetString(type.Namespace) == "ICP.Platform.Authorization.Contracts";
        });
        var signature = reader.GetBlobReader(reader.GetMemberReference(constructor).Signature);
        Assert.False(signature.ReadSignatureHeader().IsGeneric);
        Assert.Equal(4, signature.ReadCompressedInteger());
        var module = Assert.Single(reader.TypeDefinitions, handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "WorkspaceModule");
        var configure = Assert.Single(reader.GetTypeDefinition(module).GetMethods(), handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "ConfigureServices");
        var body = pe.GetMethodBody(reader.GetMethodDefinition(configure).RelativeVirtualAddress).GetILBytes()!;
        var invocation = new byte[5];
        invocation[0] = 0x73; // newobj 的内联操作数是刚核准的四参 MemberRef token。
        BitConverter.TryWriteBytes(invocation.AsSpan(1), System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(constructor));
        Assert.True(body.AsSpan().IndexOf(invocation) >= 0, "冻结旧Workspace服务注册没有真实调用四参构造。");
    }

    // 运行时目录的模块身份和实际后端路径与原字节共同核对；不在测试进程加载第二份业务程序集。
    private static async Task<PermissionModuleImage> AssertPermissionModuleImageAsync(HttpClient client, string moduleId, string version,
        string moduleRoot, string loadBoundary, string expectedHash)
    {
        var runtime = await client.GetFromJsonAsync<JsonElement>("/plugins/active-module-runtime.json");
        var module = Assert.Single(runtime.GetProperty("modules").EnumerateArray(), item => item.GetProperty("moduleId").GetString() == moduleId);
        Assert.Equal(version, module.GetProperty("version").GetString());
        var path = Path.GetFullPath(Path.Combine(moduleRoot, module.GetProperty("backend").GetProperty("entryAssembly").GetString()!));
        Assert.StartsWith(Path.GetFullPath(loadBoundary) + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        var hash = await ReadPermissionFileHashAsync(path);
        Assert.Equal(expectedHash, hash);
        return new(moduleId, version, path, hash);
    }

    // 通过正式编译功能位置写入，成功事实必须是 204，不做隐式回读或重放。
    private static async Task PutPermissionAsync(HttpClient client,
        ICP.Platform.Kernel.ModulePlanning.FeaturePoint point, string endpoint, object configuration)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Put, endpoint, configuration);
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    // null 的响应字段可按正式 JsonIgnore 合同省略；其他既有主体身份和动作必须完整保留。
    private static void AssertPermissionConfiguration(JsonElement configuration, string objectId, string entryId, string? expectedExpiry)
    {
        Assert.Equal("part.part", configuration.GetProperty("resourceKey").GetString());
        Assert.Equal(objectId, configuration.GetProperty("objectId").GetString());
        var subject = Assert.Single(configuration.GetProperty("subjects").EnumerateArray());
        Assert.Equal(entryId, subject.GetProperty("id").GetString());
        Assert.Equal("all", subject.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, subject.GetProperty("subjectId").ValueKind);
        Assert.Equal("allow", subject.GetProperty("actions").GetProperty("read").GetString());
        Assert.Equal(expectedExpiry, subject.TryGetProperty("expiresAt", out var expiry) ? expiry.GetString() : null);
    }

    // 正式 foundation 迁移必须真实增加 nullable datetime2；查询缺行或缺表都不能按 null 放过。
    private static async Task AssertPermissionExpiryAsync(FeatureCatalogHostFixture fixture, string entryId, string? expectedExpiry)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT is_nullable FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.DataScopeObjectSubject') AND name=N'ExpiresAt' AND TYPE_NAME(system_type_id)=N'datetime2';";
        Assert.True(await command.ExecuteScalarAsync() is true);
        command.CommandText = "SELECT ExpiresAt FROM dbo.DataScopeObjectSubject WHERE EID=@id;";
        command.Parameters.AddWithValue("@id", entryId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedExpiry, reader.IsDBNull(0) ? null : reader.GetDateTime(0).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(await reader.ReadAsync());
    }

    // 精确记录资源和复合对象目标，不靠 JSON 文本查找或只统计整表数量判定成功。
    private static bool IsPermissionRecord(JsonElement row, string partId) => row.GetProperty("EventType").GetString() == "dataScope.object.changed"
        && row.GetProperty("Resource").GetString() == "authorization.dataScopeObject" && row.GetProperty("TargetId").GetString() == "part.part/" + partId;

    private sealed record PermissionModuleImage(string ModuleId, string Version, string Path, string Sha256);

    [EnvironmentGatedFact("NEWP_RUN_OBJECT_HISTORY_IP_UPGRADE", "冻结Audit000001真实旧记录经正式当前Host升级000002，旧事实/双账本保持、IP为null且重启幂等")]
    public async Task PublishedAuditPrefix_AddsNullableIpWithoutRewritingExistingBusinessFacts()
    {
        using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(RequiredPath("NEWP_HISTORY_IP_UPGRADE_BASELINE_INPUT", directory: false)));
        using var current = JsonDocument.Parse(await File.ReadAllTextAsync(RequiredPath("NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT", directory: false)));
        var oldHost = ExistingPath(baseline.RootElement.GetProperty("hostRoot").GetString()!, directory: true);
        var oldPackage = ExistingPath(baseline.RootElement.GetProperty("packagePath").GetString()!, directory: false);
        var newHost = ExistingPath(current.RootElement.GetProperty("hostRoot").GetString()!, directory: true);
        var currentPackage = ExistingPath(current.RootElement.GetProperty("packagePath").GetString()!, directory: false);
        Assert.Equal(baseline.RootElement.GetProperty("packageSha256").GetString(), await ReadPermissionFileHashAsync(oldPackage));
        Assert.Equal(current.RootElement.GetProperty("packageSha256").GetString(), await ReadPermissionFileHashAsync(currentPackage));
        var versionDecisions = ReadUpgradeDecisions(oldPackage, currentPackage);
        const string tail = "20261017201106_Audit_000002_BusinessChangeIp";
        using var oldManifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(oldHost, "modules", "Audit", "migrations", "Audit.SqlServerMigrations.json")));
        using var newManifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(newHost, "modules", "Audit", "migrations", "Audit.SqlServerMigrations.json")));
        var oldNodes = oldManifest.RootElement.GetProperty("migrations").EnumerateArray().ToArray();
        var newNodes = newManifest.RootElement.GetProperty("migrations").EnumerateArray().ToArray();
        Assert.Single(oldNodes);
        Assert.Equal("20260910123205_Audit_000001_BusinessChanges", oldNodes[0].GetProperty("migrationId").GetString());
        Assert.Equal("91688d8e5c45b2e2546ca919d9b05af2f2ec9c1c7bce29ec6c15b804bb960ca5", oldNodes[0].GetProperty("contentDigest").GetString());
        Assert.True(JsonElement.DeepEquals(oldNodes[0], newNodes[0]));
        Assert.Equal(2, newNodes.Length);
        Assert.Equal(tail, newNodes[1].GetProperty("migrationId").GetString());
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async owned =>
        {
            await DeployAsync(owned, "install-native", oldPackage);
            await DeployAsync(owned, "enable-bundle", oldPackage);
            await MountBaselineMetadataAsync(owned, oldPackage);
        }, sourceHostRoot: oldHost);
        Assert.Null(await ReadIpColumnAsync(fixture));
        // 在真实旧schema上由旧Organization writer生成既有事实，不把新表的null模拟成迁移证据。
        var organization = await PostAsync(fixture, fixture.Administrator, "organization.company.initialize", "/api/organization/initialization");
        await PostAsync(fixture, fixture.Administrator, "organization.person.create", "/api/organization/persons", new {
            PersonNo = "HISTORY-IP-UPGRADE-296", ENAME = "升级前真实人员", CompanyId = organization.GetProperty("companyId").GetString(),
            DepartmentId = organization.GetProperty("departmentId").GetString(), EmploymentStatus = "Active", IsEnabled = true });
        var oldFacts = await ReadSnapshotAsync(fixture, "BusinessChange");
        Assert.Contains(oldFacts.Rows, row => row.GetProperty("EventType").GetString() == "person.created");
        var oldEf = await ReadSnapshotAsync(fixture, "__EFMigrationsHistory");
        var oldUnified = await ReadSnapshotAsync(fixture, "__NewPMigrationHistory");
        await fixture.StopAsync();
        var retired = Path.Combine(fixture.EvidenceRoot, "history-ip-baseline-host");
        var ownedRoot = Path.GetFullPath(fixture.EvidenceRoot) + Path.DirectorySeparatorChar;
        Assert.StartsWith(ownedRoot, Path.GetFullPath(fixture.HostRoot), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(ownedRoot, Path.GetFullPath(retired), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(retired));
        Directory.Move(fixture.HostRoot, retired);
        CopyTree(newHost, fixture.HostRoot);
        var statePath = Path.Combine(fixture.PluginRoot, "lifecycle/v2/state.json");
        using var beforeSelection = JsonDocument.Parse(await File.ReadAllBytesAsync(statePath));
        await SelectHistoryBuiltinsAsync(fixture, newHost, beforeSelection.RootElement.GetProperty("desiredModules"));
        using (var afterSelection = JsonDocument.Parse(await File.ReadAllBytesAsync(statePath)))
        {
            Assert.Equal(beforeSelection.RootElement.GetProperty("desiredModules").GetArrayLength(), afterSelection.RootElement.GetProperty("desiredModules").GetArrayLength());
            foreach (var previous in beforeSelection.RootElement.GetProperty("desiredModules").EnumerateArray())
            {
                var id = previous.GetProperty("moduleId").GetString();
                var actual = Assert.Single(afterSelection.RootElement.GetProperty("desiredModules").EnumerateArray(), choice => choice.GetProperty("moduleId").GetString() == id);
                Assert.Equal(previous.GetProperty("enabled").GetBoolean(), actual.GetProperty("enabled").GetBoolean());
                if (id is "Audit" or "Authorization")
                {
                    using var capsule = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(newHost, "modules", id, "plugin.json")));
                    Assert.Equal(capsule.RootElement.GetProperty("version").GetString(), actual.GetProperty("version").GetString());
                }
                else Assert.Equal(previous.GetProperty("version").GetString(), actual.GetProperty("version").GetString());
            }
            foreach (var pointer in new[] { "activeSnapshotId", "lastKnownGoodSnapshotId" })
                Assert.Equal(beforeSelection.RootElement.GetProperty(pointer).GetRawText(), afterSelection.RootElement.GetProperty(pointer).GetRawText());
        }
        // 本票升级旧数据与完整当前发行组合；新Audit合同不承诺继续装载旧native IL。
        // 只退役本fixture给旧provider挂载的metadata副本，原历史包、Host和旧库存均保留。
        RetireBaselineAuthoring(fixture);
        await DeployAsync(fixture, "install-native", currentPackage);
        var decisionsPath = Path.Combine(fixture.EvidenceRoot, "history-upgrade-version-decisions.json");
        await File.WriteAllBytesAsync(decisionsPath, JsonSerializer.SerializeToUtf8Bytes(versionDecisions, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await DeployAsync(fixture, "enable-bundle", currentPackage, decisionsPath);
        using (var selected = JsonDocument.Parse(await File.ReadAllBytesAsync(statePath)))
        {
            foreach (var previous in beforeSelection.RootElement.GetProperty("desiredModules").EnumerateArray())
            {
                var id = previous.GetProperty("moduleId").GetString();
                var actual = Assert.Single(selected.RootElement.GetProperty("desiredModules").EnumerateArray(), choice => choice.GetProperty("moduleId").GetString() == id);
                Assert.Equal(previous.GetProperty("enabled").GetBoolean(), actual.GetProperty("enabled").GetBoolean());
                if (id is not ("Audit" or "Authorization"))
                {
                    var decision = versionDecisions.SingleOrDefault(choice => choice.ModuleId == id);
                    Assert.Equal(decision?.Version ?? previous.GetProperty("version").GetString(), actual.GetProperty("version").GetString());
                }
            }
            foreach (var pointer in new[] { "activeSnapshotId", "lastKnownGoodSnapshotId" })
                Assert.Equal(beforeSelection.RootElement.GetProperty(pointer).GetRawText(), selected.RootElement.GetProperty(pointer).GetRawText());
        }
        await fixture.StartAsync();
        Assert.Equal("nvarchar:512:1", await ReadIpColumnAsync(fixture));
        Assert.Equal(oldFacts.Rows, (await ReadSnapshotAsync(fixture, oldFacts.Table, oldFacts.Columns)).Rows, JsonRowsComparer.Instance);
        var facts = await ReadSnapshotAsync(fixture, "BusinessChange");
        Assert.All(facts.Rows, row => Assert.Equal(JsonValueKind.Null, row.GetProperty("IpAddress").ValueKind));
        var upgradedEf = await ReadSnapshotAsync(fixture, oldEf.Table, oldEf.Columns);
        var upgradedUnified = await ReadSnapshotAsync(fixture, oldUnified.Table, oldUnified.Columns);
        Assert.Equal(oldEf.Rows, upgradedEf.Rows.Where(row => row.GetProperty("MigrationId").GetString() != tail), JsonRowsComparer.Instance);
        AssertPrefix(oldUnified, upgradedUnified);
        Assert.Equal(oldEf.Rows.Length + 1, upgradedEf.Rows.Length);
        Assert.Equal(oldUnified.Rows.Length + 1, upgradedUnified.Rows.Length);
        Assert.Single(upgradedUnified.Rows, row => row.GetProperty("OwnerModuleId").GetString() == "Audit" && row.GetProperty("MigrationId").GetString() == tail);
        await fixture.StopAsync();
        await fixture.StartAsync();
        Assert.Equal(facts.Rows, (await ReadSnapshotAsync(fixture, facts.Table, facts.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(upgradedEf.Rows, (await ReadSnapshotAsync(fixture, oldEf.Table, oldEf.Columns)).Rows, JsonRowsComparer.Instance);
        Assert.Equal(upgradedUnified.Rows, (await ReadSnapshotAsync(fixture, oldUnified.Table, oldUnified.Columns)).Rows, JsonRowsComparer.Instance);
    }

    [EnvironmentGatedFact("NEWP_RUN_OWNERSHIP_TRANSFER_UPGRADE", "冻结296完整旧部署升级通知000009，旧对象/引用/审计保留、真实接收及重启")]
    public async Task PublishedWorkspacePrefix_AddsTransferNotifications_PreservesFactsAndRestart()
    {
        using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(RequiredPath("NEWP_TRANSFER_UPGRADE_BASELINE_INPUT", false)));
        using var current = JsonDocument.Parse(await File.ReadAllTextAsync(RequiredPath("NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT", false)));
        var oldHost = ExistingPath(baseline.RootElement.GetProperty("hostRoot").GetString()!, true);
        var oldPackage = ExistingPath(baseline.RootElement.GetProperty("packagePath").GetString()!, false);
        var newHost = ExistingPath(current.RootElement.GetProperty("hostRoot").GetString()!, true);
        var newPackage = ExistingPath(current.RootElement.GetProperty("packagePath").GetString()!, false);
        Assert.Equal(baseline.RootElement.GetProperty("packageSha256").GetString(), await ReadPermissionFileHashAsync(oldPackage));
        Assert.Equal(current.RootElement.GetProperty("packageSha256").GetString(), await ReadPermissionFileHashAsync(newPackage));
        var decisions = ReadUpgradeDecisions(oldPackage, newPackage);
        const string tail = "20261017231111_Workspace_000009_TransferNotifications";
        using (var archive = ZipFile.OpenRead(oldPackage))
        {
            var entry = Assert.Single(archive.Entries, item => item.FullName == "modules/Workspace/migrations/Workspace.SqlServerMigrations.json");
            using var stream = entry.Open();
            using var manifest = await JsonDocument.ParseAsync(stream);
            Assert.Equal(8, manifest.RootElement.GetProperty("migrations").GetArrayLength());
            Assert.DoesNotContain(manifest.RootElement.GetProperty("migrations").EnumerateArray(), node => node.GetProperty("migrationId").GetString() == tail);
        }
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async owned =>
        {
            await DeployAsync(owned, "install-native", oldPackage);
            await DeployAsync(owned, "enable-bundle", oldPackage);
            await MountBaselineMetadataAsync(owned, oldPackage);
        }, sourceHostRoot: oldHost);
        Assert.False(await TransferNotificationTableExistsAsync(fixture));
        var organization = await PostAsync(fixture, fixture.Administrator, "organization.company.initialize", "/api/organization/initialization");
        await PostAsync(fixture, fixture.Administrator, "organization.person.create", "/api/organization/persons", new {
            PersonNo = "TRANSFER-UPGRADE-299", ENAME = "转交升级前人员", CompanyId = organization.GetProperty("companyId").GetString(),
            DepartmentId = organization.GetProperty("departmentId").GetString(), EmploymentStatus = "Active", IsEnabled = true });
        var recipient = await PostAsync(fixture, fixture.Administrator, "identity.account.create", "/api/identity/accounts",
            new { ECODE = "transfer-upgrade-recipient", ENAME = "转交升级接收人" });
        var recipientId = recipient.GetProperty("account").GetProperty("eid").GetString()!;
        var root = await PostAsync(fixture, fixture.Administrator, "workspace.spaces.access", "/api/workspace/spaces/public/initialize");
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var rootPosition = new { spaceId = root.GetProperty("spaceId").GetString()!, rootFolderId = rootId, linkIds = Array.Empty<string>() };
        await PostAsync(fixture, fixture.Administrator, "workspace.folder.create", "/api/workspace/content/folders",
            new { position = rootPosition, values = new { ENAME = "升级前原对象容器" } });
        var rootContents = await ReadContentsAsync(fixture, fixture.Administrator, rootPosition.spaceId, rootId, rootId, []);
        var container = Assert.Single(rootContents.GetProperty("items").EnumerateArray(), row => row.GetProperty("fields").GetProperty("Name").GetString() == "升级前原对象容器");
        var containerId = container.GetProperty("associatedObject").GetProperty("id").GetString()!;
        var position = new { rootPosition.spaceId, rootFolderId = rootId, linkIds = new[] { container.GetProperty("linkId").GetString()! } };
        var targets = new List<(string Model, string Resource, string Id, string LinkId)>();
        foreach (var (model, resource, kind, behavior) in new[] {
            ("WorkspaceFolder", "workspace.folder", "folders", "workspace.folder.create"),
            ("Part", "part.part", "parts", "workspace.folder.createPart"),
            ("Document", "document.document", "documents", "workspace.folder.createDocument") })
        {
            var name = "转交升级原对象-" + model;
            object values = model == "WorkspaceFolder" ? new { ENAME = name } : new { ENAME = name, ECODE = "TRANSFER-UPGRADE-" + model };
            await PostAsync(fixture, fixture.Administrator, behavior, "/api/workspace/content/" + kind, new { position, values });
            var contents = await ReadContentsAsync(fixture, fixture.Administrator, rootPosition.spaceId, rootId, containerId, position.linkIds);
            var row = Assert.Single(contents.GetProperty("items").EnumerateArray(), item => item.GetProperty("fields").GetProperty("Name").GetString() == name);
            targets.Add((model, resource, row.GetProperty("associatedObject").GetProperty("id").GetString()!, row.GetProperty("linkId").GetString()!));
        }
        var oldFacts = new List<TableSnapshot>();
        foreach (var table in new[] { "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "Part", "Document", "BusinessChange" })
        {
            var snapshot = await ReadSnapshotAsync(fixture, table);
            Assert.NotEmpty(snapshot.Rows);
            oldFacts.Add(snapshot);
        }
        Assert.Contains(oldFacts.Single(item => item.Table == "BusinessChange").Rows, row => row.GetProperty("EventType").GetString() == "person.created");
        var oldEf = await ReadSnapshotAsync(fixture, "__EFMigrationsHistory");
        var oldUnified = await ReadSnapshotAsync(fixture, "__NewPMigrationHistory");
        Assert.NotEmpty(oldEf.Rows); Assert.NotEmpty(oldUnified.Rows);
        await fixture.StopAsync();
        var retiredHost = Path.Combine(fixture.EvidenceRoot, "transfer-baseline-host");
        var ownedRoot = Path.GetFullPath(fixture.EvidenceRoot) + Path.DirectorySeparatorChar;
        Assert.StartsWith(ownedRoot, Path.GetFullPath(fixture.HostRoot), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(ownedRoot, Path.GetFullPath(retiredHost), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(retiredHost));
        Directory.Move(fixture.HostRoot, retiredHost);
        CopyTree(newHost, fixture.HostRoot);
        var statePath = Path.Combine(fixture.PluginRoot, "lifecycle/v2/state.json");
        using var previousState = JsonDocument.Parse(await File.ReadAllBytesAsync(statePath));
        await SelectTransferBuiltinsAsync(fixture, newHost, previousState.RootElement.GetProperty("desiredModules"));
        RetireBaselineAuthoring(fixture);
        // 新Host与完整当前native先配齐，不以旧共享DLL混装测试本次迁移。
        await DeployAsync(fixture, "install-native", newPackage);
        var decisionsPath = Path.Combine(fixture.EvidenceRoot, "transfer-version-decisions.json");
        await File.WriteAllBytesAsync(decisionsPath, JsonSerializer.SerializeToUtf8Bytes(decisions, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await DeployAsync(fixture, "enable-bundle", newPackage, decisionsPath);
        await fixture.StartAsync();
        Assert.True(await TransferNotificationTableExistsAsync(fixture));
        var notifications = await ReadSnapshotAsync(fixture, "WorkspaceTransferNotification");
        Assert.Empty(notifications.Rows);
        Assert.Contains("RecipientId", notifications.Columns); Assert.Contains("ObjectId", notifications.Columns);
        foreach (var snapshot in oldFacts)
            AssertRetained(snapshot, await ReadSnapshotAsync(fixture, snapshot.Table, snapshot.Columns));
        var upgradedEf = await ReadSnapshotAsync(fixture, oldEf.Table, oldEf.Columns);
        var upgradedUnified = await ReadSnapshotAsync(fixture, oldUnified.Table, oldUnified.Columns);
        AssertRetained(oldEf, upgradedEf); AssertPrefix(oldUnified, upgradedUnified);
        Assert.Single(upgradedEf.Rows, row => row.GetProperty("MigrationId").GetString() == tail);
        Assert.Single(upgradedUnified.Rows, row => row.GetProperty("OwnerModuleId").GetString() == "Workspace" && row.GetProperty("MigrationId").GetString() == tail);
        var receipt = await PostAsync(fixture, fixture.Administrator, "workspace.folder.transfer", "/api/workspace/ownership/transfer",
            new { position, linkIds = targets.Select(target => target.LinkId).ToArray(), recipientId });
        Assert.Equal(3, receipt.GetProperty("changed").GetInt32());
        notifications = await ReadSnapshotAsync(fixture, "WorkspaceTransferNotification");
        Assert.Equal(3, notifications.Rows.Length);
        Assert.All(notifications.Rows, row => Assert.Equal(recipientId, row.GetProperty("RecipientId").GetString()));
        foreach (var target in targets)
        {
            var old = oldFacts.Single(snapshot => snapshot.Table == target.Model);
            var before = Assert.Single(old.Rows, row => row.GetProperty("EID").GetString() == target.Id);
            var after = await ReadSnapshotAsync(fixture, target.Model, old.Columns);
            var row = Assert.Single(after.Rows, row => row.GetProperty("EID").GetString() == target.Id);
            Assert.Equal(recipientId, row.GetProperty("OwnerId").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("OwnerDepartmentId").ValueKind);
            foreach (var column in new[] { "CreatedBy", "CreatedAt" }) Assert.Equal(before.GetProperty(column).GetRawText(), row.GetProperty(column).GetRawText());
            Assert.Single(notifications.Rows, item => item.GetProperty("ResourceKey").GetString() == target.Resource && item.GetProperty("ObjectId").GetString() == target.Id);
        }
        var links = await ReadSnapshotAsync(fixture, "WorkspaceFolderContent");
        AssertRetained(oldFacts.Single(item => item.Table == links.Table), links);
        foreach (var notification in notifications.Rows)
        {
            var link = Assert.Single(links.Rows, row => row.GetProperty("LID").GetString() == notification.GetProperty("LinkId").GetString());
            Assert.Equal(notification.GetProperty("DirectoryId").GetString(), link.GetProperty("PEID").GetString());
            Assert.Equal(notification.GetProperty("ObjectId").GetString(), link.GetProperty("CEID").GetString());
        }
        var audit = await ReadSnapshotAsync(fixture, "BusinessChange");
        AssertRetained(oldFacts.Single(item => item.Table == audit.Table), audit);
        foreach (var target in targets) Assert.Single(audit.Rows, row => row.GetProperty("EventType").GetString() == "entity.ownership.transferred"
            && row.GetProperty("Resource").GetString() == target.Resource && row.GetProperty("TargetId").GetString() == target.Id);
        var beforeRestart = new List<TableSnapshot>();
        foreach (var table in oldFacts.Select(item => item.Table).Append("WorkspaceTransferNotification").Append(oldEf.Table).Append(oldUnified.Table))
            beforeRestart.Add(await ReadSnapshotAsync(fixture, table));
        await fixture.StopAsync(); await fixture.StartAsync();
        foreach (var snapshot in beforeRestart)
            Assert.Equal(snapshot.Rows, (await ReadSnapshotAsync(fixture, snapshot.Table, snapshot.Columns)).Rows, JsonRowsComparer.Instance);
        output.WriteLine("Transfer upgrade: prefix8 -> 000009; old objects/links/audit retained; changed3/received3; restart retained all facts.");
    }

    private static async Task<bool> TransferNotificationTableExistsAsync(FeatureCatalogHostFixture fixture)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.WorkspaceTransferNotification',N'U') IS NULL THEN 0 ELSE 1 END;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task SelectTransferBuiltinsAsync(FeatureCatalogHostFixture fixture, string currentHost, JsonElement previousDesired)
    {
        var selections = new List<PluginDesiredModuleState>();
        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(currentHost, "modules")).Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, "plugin.json");
            if (!File.Exists(path)) continue;
            using var capsule = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var id = capsule.RootElement.GetProperty("moduleId").GetString()!;
            var previous = previousDesired.EnumerateArray().SingleOrDefault(item => item.GetProperty("moduleId").GetString() == id);
            if (previous.ValueKind == JsonValueKind.Undefined) continue;
            var version = capsule.RootElement.GetProperty("version").GetString()!;
            if (version != previous.GetProperty("version").GetString()) selections.Add(new(id, version, previous.GetProperty("enabled").GetBoolean()));
        }
        Assert.Contains(selections, choice => choice.ModuleId == "Organization");
        Assert.Contains(selections, choice => choice.ModuleId == "Authorization");
        var pathToSelection = Path.Combine(fixture.EvidenceRoot, "transfer-builtin-desired.json");
        await File.WriteAllBytesAsync(pathToSelection, JsonSerializer.SerializeToUtf8Bytes(selections, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = fixture.HostRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(fixture.HostRoot, "HostApi.dll"), "plugin-deploy", "desired", "--file", pathToSelection }) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__SqlServer"] = fixture.ConnectionString;
        start.Environment["Platform__PluginInstallRoot"] = fixture.PluginRoot;
        start.Environment["Platform__ExternalPluginsEnabled"] = "false";
        start.Environment["Files__StorageRoot"] = fixture.StorageRoot;
        start.Environment["Authentication__Host__KeyRingPath"] = Path.Combine(Path.GetDirectoryName(fixture.HostRoot)!, "keys");
        start.Environment["Identity__Credentials__UseSharedDevelopmentCredential"] = "false";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动正式转交升级内建选版。");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3)); Assert.Equal(0, process.ExitCode); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            string Sanitize(string text) => text.Replace(fixture.ConnectionString, "[connection]", StringComparison.Ordinal).Replace(fixture.AdministratorPassword, "[credential]", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "transfer-select-builtins.stdout.log"), Sanitize(await stdout));
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "transfer-select-builtins.stderr.log"), Sanitize(await stderr));
        }
        using var selected = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(fixture.PluginRoot, "lifecycle/v2/state.json")));
        foreach (var previous in previousDesired.EnumerateArray())
        {
            var actual = Assert.Single(selected.RootElement.GetProperty("desiredModules").EnumerateArray(), item => item.GetProperty("moduleId").GetString() == previous.GetProperty("moduleId").GetString());
            Assert.Equal(previous.GetProperty("enabled").GetBoolean(), actual.GetProperty("enabled").GetBoolean());
            Assert.Equal(selections.SingleOrDefault(choice => choice.ModuleId == previous.GetProperty("moduleId").GetString())?.Version
                ?? previous.GetProperty("version").GetString(), actual.GetProperty("version").GetString());
        }
    }

    private static async Task<string?> ReadIpColumnAsync(FeatureCatalogHostFixture fixture)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONCAT(TYPE_NAME(user_type_id),N':',max_length,N':',is_nullable) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.BusinessChange') AND name=N'IpAddress';";
        return await command.ExecuteScalarAsync() as string;
    }

    // 两个builtin同时换版必须由现成desired命令原子选择；每项enabled显式保持旧值，不改库存。
    private static async Task SelectHistoryBuiltinsAsync(FeatureCatalogHostFixture fixture, string currentHost, JsonElement previousDesired)
    {
        var selections = new List<PluginDesiredModuleState>();
        foreach (var module in new[] { "Audit", "Authorization" })
        {
            using var capsule = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(currentHost, "modules", module, "plugin.json")));
            var previous = Assert.Single(previousDesired.EnumerateArray(), choice => choice.GetProperty("moduleId").GetString() == module);
            selections.Add(new(module, capsule.RootElement.GetProperty("version").GetString()!, previous.GetProperty("enabled").GetBoolean()));
        }
        var selectionsPath = Path.Combine(fixture.EvidenceRoot, "history-builtin-desired.json");
        await File.WriteAllTextAsync(selectionsPath, JsonSerializer.Serialize(selections, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = fixture.HostRoot, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(fixture.HostRoot, "HostApi.dll"), "plugin-deploy", "desired", "--file", selectionsPath }) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__SqlServer"] = fixture.ConnectionString;
        start.Environment["Platform__PluginInstallRoot"] = fixture.PluginRoot;
        start.Environment["Platform__ExternalPluginsEnabled"] = "false";
        start.Environment["Files__StorageRoot"] = fixture.StorageRoot;
        start.Environment["Authentication__Host__KeyRingPath"] = Path.Combine(Path.GetDirectoryName(fixture.HostRoot)!, "keys");
        start.Environment["Identity__Credentials__UseSharedDevelopmentCredential"] = "false";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动正式内建组件选版。");
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
            string Sanitize(string text) => text.Replace(fixture.ConnectionString, "[connection]", StringComparison.Ordinal).Replace(fixture.AdministratorPassword, "[credential]", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "history-select-builtins.stdout.log"), Sanitize(await stdout));
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "history-select-builtins.stderr.log"), Sanitize(await stderr));
        }
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

    internal static async Task DeployAsync(FeatureCatalogHostFixture fixture, string operation, string package, string? decisionsPath = null)
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
