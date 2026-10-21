using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Acceptance;

internal sealed record WorkspaceAcceptanceActor(string AccountId, string ECode, string Password);
internal sealed record WorkspaceFolderPosition(string SpaceId, string RootFolderId, string FolderId, string[] LinkIds)
{
    internal object CommandPosition => new { spaceId = SpaceId, rootFolderId = RootFolderId, linkIds = LinkIds };
}
internal sealed record WorkspaceOccurrenceFixture(WorkspaceFolderPosition PublicRoot, string ALinkId, string[] Labels, string SampleName);

/// <summary>通过正式 Identity、Organization 和 Authorization API 准备指定功能集合的普通账号。</summary>
internal static class WorkspaceAcceptanceFixture
{
    internal const string CookieName = "ICP.Platform.WorkspaceAcceptance";

    internal static HttpClient CreateClient(int port) => new(new HttpClientHandler { CookieContainer = new CookieContainer() })
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}")
    };

    internal static async Task LoginAndChangePasswordAsync(HttpClient client, string ecode, string temporaryPassword,
        string permanentPassword, CancellationToken token)
    {
        var login = await SendAsync(client, HttpMethod.Post, "/api/auth/login",
            new { ecode, password = temporaryPassword }, null, token);
        await SendAsync(client, HttpMethod.Post, "/api/identity/me/password-change",
            new { currentPassword = temporaryPassword, newPassword = permanentPassword, expectedRevision = login.GetProperty("revision").GetInt64() }, null, token);
    }

    internal static Task<JsonElement> LoginAsync(HttpClient client, WorkspaceAcceptanceActor actor, CancellationToken token) =>
        SendAsync(client, HttpMethod.Post, "/api/auth/login", new { ecode = actor.ECode, password = actor.Password }, null, token);

    // 在正式 Host 的 API 层证明新组合来自本次 authoring，浏览器不判断身份或数据语义。
    internal static async Task VerifyInforCenterCompositionAsync(HttpClient client, CancellationToken token)
    {
        var catalog = await SendAsync(client, HttpMethod.Get, "/plugins/active-module-runtime.json", null, null, token);
        var modules = catalog.GetProperty("modules").EnumerateArray().ToArray();
        var parent = modules.Single(module => module.GetProperty("moduleId").GetString() == "InforCenter");
        if (!parent.GetProperty("compositionMembers").EnumerateArray().Select(member => member.GetString()).SequenceEqual(["Document", "Part", "Workspace"])
            || parent.GetProperty("modelKeys").GetArrayLength() != 0
            || parent.GetProperty("backend").ValueKind != JsonValueKind.Null
            || parent.GetProperty("frontend").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("正式 InforCenter 组合未保留直接成员或意外取得业务模型/实现。");
        foreach (var id in new[] { "Workspace", "Part", "Document" })
        {
            var member = modules.Single(module => module.GetProperty("moduleId").GetString() == id);
            if (member.GetProperty("parentModuleId").GetString() != "InforCenter"
                || member.GetProperty("frontend").GetProperty("behaviorKey").GetString() != id.ToLowerInvariant() + ".frontend")
                throw new InvalidOperationException($"正式组合成员 {id} 的父组件或 behavior owner 已改变。");
        }
    }

    // 普通主体经真实创建、Link 查询和所属详情证明 SQL/授权消费链，不使用手工持久化计划。
    internal static async Task VerifyPartCreationAsync(HttpClient client, AcceptanceFeatureCatalog features, JsonElement root,
        string runId, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var code = "WS-COMPOSITION-" + runId[..8];
        var name = "组合 API 零组件 " + runId[..8];
        var position = await CreateFolderAtPositionAsync(client, features,
            new(root.GetProperty("spaceId").GetString()!, rootId, rootId, []), "组合 API 文件夹 " + runId[..8], token);
        var receipt = await SendAsync(client, HttpMethod.Post, "/api/workspace/content/parts",
            new { position = position.CommandPosition,
                values = new { ECODE = code, ENAME = name } },
            features.Action(entry, "createPart", "list.pageActions", "folder").Position, token,
            buttonId: features.Button(features.Action(entry, "createPart", "list.pageActions", "folder")));
        if (!receipt.GetProperty("succeeded").GetBoolean() || receipt.TryGetProperty("id", out _))
            throw new InvalidOperationException("组合后的真实 Part 创建未返回受控成功回执。");
        var contents = await ReadContentsAtPositionAsync(client, position, entry.Position, token, features);
        var row = contents.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("fields").GetProperty("Name").GetString() == name);
        var target = row.GetProperty("associatedObject");
        var id = target.GetProperty("id").GetString()!;
        if (target.GetProperty("modelKey").GetString() != "Part"
            || row.GetProperty("parent").GetProperty("modelKey").GetString() != "WorkspaceFolder"
            || row.GetProperty("parent").GetProperty("id").GetString() != position.FolderId
            || string.IsNullOrEmpty(row.GetProperty("linkId").GetString()))
            throw new InvalidOperationException("组合后的内容 Link 未保留 Workspace/Part 端点与真实身份。");
        var detail = await SendAsync(client, HttpMethod.Get, "/api/part/parts/" + Uri.EscapeDataString(id), null,
            features.Behavior("Part", "part.part.read").Position, token,
            features.ReadDeclaration("Part", "part.part.detail", features.Behavior("Part", "part.part.read").Position));
        var fields = detail.GetProperty("fields");
        if (fields.GetProperty("EID").GetString() != id || fields.GetProperty("ECODE").GetString() != code || fields.GetProperty("ENAME").GetString() != name)
            throw new InvalidOperationException("组合后的所属 Part 详情未保持正式对象字段与身份。");
    }

    internal static async Task<WorkspaceAcceptanceActor> PrepareAsync(HttpClient administrator,
        AcceptanceFeatureCatalog features, string runId, Action<string> registerSecret,
        IReadOnlyList<FeaturePoint> grantedFeatures, CancellationToken token,
        PublishedWorkspacePreparation preparation = PublishedWorkspacePreparation.CurrentRelease)
    {
        var suffix = runId[..8];
        var ecode = $"workspace.{suffix}";
        var account = await SendPreparationCommandAsync(HttpMethod.Post, "/api/identity/accounts",
            new { ECODE = ecode, ENAME = $"工作空间验收账号 {suffix}" },
            features.Action(features.Behavior("Identity", "identity.account.read"), "create", "list.pageActions"));
        var accountData = account.GetProperty("account");
        var accountId = accountData.GetProperty("eid").GetString()!;
        var temporaryCredential = account.GetProperty("temporaryCredential").GetString()!;
        registerSecret(temporaryCredential);
        var password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        registerSecret(password);

        var company = await SendPreparationCommandAsync(HttpMethod.Post, "/api/organization/companies",
            new { ECODE = $"WS-{suffix}", ENAME = $"工作空间验收公司 {suffix}", SortOrder = "9999" },
            features.Action(features.OrganizationTab("companies"), "create", "list.pageActions"),
            () => features.Declaration(features.Action(features.OrganizationTab("companies"), "create", "list.pageActions"),
                "organization.company.create", "standardCrud.create", "organization.company"));
        var companyId = EntityId(company);
        var department = await SendPreparationCommandAsync(HttpMethod.Post, "/api/organization/departments",
            new { ECODE = $"WS-{suffix}", ENAME = $"工作空间验收部门 {suffix}", CompanyId = companyId, SortOrder = "9999" },
            features.Action(features.OrganizationTab("departments"), "create", "list.pageActions"),
            () => features.Declaration(features.Action(features.OrganizationTab("departments"), "create", "list.pageActions"),
                "organization.department.create", "standardCrud.create", "organization.department"));
        var person = await SendPreparationCommandAsync(HttpMethod.Post, "/api/organization/persons",
            new { PersonNo = $"WS-{suffix}", ENAME = $"工作空间验收人员 {suffix}", CompanyId = companyId,
                DepartmentId = EntityId(department), EmploymentStatus = "Active", IsEnabled = true },
            features.Action(features.OrganizationTab("persons"), "create", "list.pageActions"),
            () => features.Declaration(features.Action(features.OrganizationTab("persons"), "create", "list.pageActions"),
                "organization.person.create", "standardCrud.create", "organization.person"));
        await SendPreparationCommandAsync(HttpMethod.Put, $"/api/organization/persons/{EntityId(person)}/user-binding",
            new { accountEid = accountId, expectedRevision = accountData.GetProperty("revision").GetInt64(), reason = "准备工作空间普通验收主体" },
            features.Action(features.OrganizationTab("persons"), "linkUser", "detail.custom"));

        var roleCode = $"WorkspaceGate{suffix}";
        var roleEntry = features.Behavior("Authorization", "authorization.role.read");
        var role = await SendPreparationCommandAsync(HttpMethod.Post, "/api/authorization/roles",
            new { Code = roleCode, Name = $"工作空间验收角色 {suffix}" },
            features.Action(roleEntry, "create", "list.pageActions"));
        var roleId = role.GetProperty("id").GetString()!;
        await SendPreparationCommandAsync(HttpMethod.Put, $"/api/authorization/workbench/roles/{roleId}",
            new { positions = features.GrantSet(grantedFeatures.ToArray()) }, features.Behavior("Authorization", "authorization.workbench.save"));
        await SendPreparationCommandAsync(HttpMethod.Put, $"/api/authorization/users/{accountId}/roles",
            new { RoleCodes = new[] { roleCode } }, features.Action(roleEntry, "userRoles", "list.rowActions"));
        using var ordinary = CreateClient(administrator.BaseAddress!.Port);
        await LoginAndChangePasswordAsync(ordinary, ecode, temporaryCredential, password, token);
        return new(accountId, ecode, password);

        // 历史升级输入保持冻结；当前发行物的准备必须使用同一正式目录中的实际按钮。
        Task<JsonElement> SendPreparationCommandAsync(HttpMethod method, string path, object body, FeaturePoint point,
            Func<string>? historicalDeclaration = null) => preparation == PublishedWorkspacePreparation.HistoricalUpgrade
                ? SendAsync(administrator, method, path, body, point.Position, token, historicalDeclaration?.Invoke())
                : SendButtonAsync(administrator, features, point, method, path, body, token);
    }

    // 只用当前普通主体的正式命令准备个人来源孤立对象；移除的是查询返回的 LID，不是实体 EID。
    internal static async Task<string> PrepareOrphanFolderAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        string name, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        await SendAsync(ordinary, HttpMethod.Post, "/api/workspace/spaces/personal/ensure", null, entry.Position, token);
        var spaces = await SendAsync(ordinary, HttpMethod.Get, "/api/workspace/spaces", null, entry.Position, token);
        var personal = spaces.EnumerateArray().Single(space => space.GetProperty("spaceKind").GetString() == "Personal");
        var spaceId = personal.GetProperty("spaceId").GetString()!;
        var rootFolderId = personal.GetProperty("rootFolderId").GetString()!;
        var position = new { spaceId, rootFolderId, linkIds = Array.Empty<string>() };
        var created = await CreateFolderAtPositionAsync(ordinary, features, new(spaceId, rootFolderId, rootFolderId, []), name, token);
        await SendButtonAsync(ordinary, features, features.Action(entry, "remove", "list.rowActions", "folder"),
            HttpMethod.Delete, $"/api/workspace/content/references/{Uri.EscapeDataString(created.LinkIds[^1])}", new { position }, token);
        return created.FolderId;
    }

    // 多父恢复样本全部走当前普通主体正式命令；仅记录关系链，不在测试侧编码 Provider nodeKey。
    internal static async Task<WorkspaceOccurrenceFixture> PrepareOccurrenceGraphAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        string runId, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        await SendAsync(ordinary, HttpMethod.Post, "/api/workspace/spaces/personal/ensure", null, entry.Position, token);
        var roots = await SendAsync(ordinary, HttpMethod.Get, "/api/workspace/spaces", null, entry.Position, token);
        WorkspaceFolderPosition Root(string kind)
        {
            var root = roots.EnumerateArray().Single(item => item.GetProperty("spaceKind").GetString() == kind);
            var id = root.GetProperty("rootFolderId").GetString()!;
            return new(root.GetProperty("spaceId").GetString()!, id, id, []);
        }
        var publicRoot = Root("Public");
        var suffix = runId[..8];
        var a = await CreateFolderAtPositionAsync(ordinary, features, publicRoot, $"路径A {suffix}", token);
        var b = await CreateFolderAtPositionAsync(ordinary, features, publicRoot, $"路径B {suffix}", token);
        var shared = await CreateFolderAtPositionAsync(ordinary, features, a, $"共享S {suffix}", token);
        var terminal = await CreateFolderAtPositionAsync(ordinary, features, shared, $"后代T {suffix}", token);
        var sampleName = $"恢复内容 {suffix}";
        await CreateFolderAtPositionAsync(ordinary, features, terminal, sampleName, token);
        foreach (var parent in new[] { b, Root("Personal") })
            await SendAsync(ordinary, HttpMethod.Post, "/api/workspace/content/references",
                new { position = parent.CommandPosition, target = new { modelKey = "WorkspaceFolder", id = shared.FolderId } },
                features.Action(entry, "add", "list.custom", "folder").Position, token);
        return new(publicRoot, a.LinkIds[^1], ["公共空间", $"路径A {suffix}", $"共享S {suffix}", $"后代T {suffix}"], sampleName);
    }

    internal static async Task<WorkspaceFolderPosition> CreateFolderAtPositionAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        WorkspaceFolderPosition parent, string name, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        var created = await SendAsync(ordinary, HttpMethod.Post, "/api/workspace/content/folders",
            new { position = parent.CommandPosition, values = new { ENAME = name } },
            features.Action(entry, "create", "list.pageActions", "folder").Position, token,
            buttonId: features.Button(features.Action(entry, "create", "list.pageActions", "folder")));
        var id = created.GetProperty("id").GetString()!;
        var contents = await ReadContentsAtPositionAsync(ordinary, parent, entry.Position, token, features);
        var link = contents.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("associatedObject").GetProperty("id").GetString() == id);
        return parent with { FolderId = id, LinkIds = [.. parent.LinkIds, link.GetProperty("linkId").GetString()!] };
    }

    internal static Task<JsonElement> InvalidateOccurrenceAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        WorkspaceOccurrenceFixture fixture, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        return SendButtonAsync(ordinary, features, features.Action(entry, "remove", "list.rowActions", "folder"), HttpMethod.Delete,
            $"/api/workspace/content/references/{Uri.EscapeDataString(fixture.ALinkId)}", new { position = fixture.PublicRoot.CommandPosition }, token);
    }

    internal static Task<JsonElement> ReadDirectReferencesAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        string modelKey, string id, CancellationToken token) => SendAsync(ordinary, HttpMethod.Get,
        $"/api/links/workspace.content?direction=reverse&anchorModelKey={Uri.EscapeDataString(modelKey)}&anchorId={Uri.EscapeDataString(id)}",
        null, features.Behavior("Workspace", "workspace.spaces.access").Position, token,
        features.ReadDeclaration("Workspace", "workspace.references.list", features.Behavior("Workspace", "workspace.spaces.access").Position));

    // 元数据对象由已授权的普通主体正式创建，实体字段契约沿用所属模块。
    internal static async Task<string> CreateMetadataObjectAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        string module, string behavior, string path, string code, string name, CancellationToken token)
    {
        var entry = features.Behavior(module, behavior);
        var create = features.Action(entry, "create", "list.pageActions");
        if (module is not ("Part" or "Document"))
            throw new InvalidOperationException("元数据准备只接受已登记的Part/Document来源。");
        var result = await SendButtonAsync(ordinary, features, create, HttpMethod.Post, path, new { ECODE = code, ENAME = name }, token);
        // 受控成功回执不含可引用身份，不能伪造 ID 继续准备。
        return result.GetProperty("fields").GetProperty("EID").GetString()
            ?? throw new InvalidOperationException("正式元数据创建未返回可引用的 EID。");
    }

    // 重启场景独立拥有三类样本，不借用浏览器旅程可能已经删除的对象；沿用普通主体的 Workspace 创建权限。
    internal static async Task<WorkspaceFolderPosition> PrepareRestartContentAsync(HttpClient ordinary, AcceptanceFeatureCatalog features,
        JsonElement roots, string runId, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        var root = roots.EnumerateArray().Single(item => item.GetProperty("spaceKind").GetString() == "Public");
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var position = new WorkspaceFolderPosition(root.GetProperty("spaceId").GetString()!, rootId, rootId, []);
        var suffix = runId[..8];
        var folder = await CreateFolderAtPositionAsync(ordinary, features, position, $"重启文件夹 {suffix}", token);
        foreach (var (action, path, code, name) in new[]
        {
            ("createPart", "/api/workspace/content/parts", $"WS-R-P-{suffix}", $"重启零组件 {suffix}"),
            ("createDocument", "/api/workspace/content/documents", $"WS-R-D-{suffix}", $"重启图文档 {suffix}")
        })
            await SendButtonAsync(ordinary, features, features.Action(entry, action, "list.pageActions", "folder"), HttpMethod.Post, path,
                new { position = folder.CommandPosition, values = new { ECODE = code, ENAME = name } }, token);
        // 创建回执不含实体或关系身份；重启前后的快照只消费正式 Link 查询。
        return folder;
    }

    // 根只接收文件夹；同时读取样本普通文件夹内的业务引用，不用空根或部分页证明持久性。
    internal static async Task<string[]> ReadRestartContentAsync(HttpClient client, JsonElement roots, string entryPosition,
        WorkspaceFolderPosition sampleFolder, CancellationToken token, AcceptanceFeatureCatalog? features = null)
    {
        features ??= await AcceptanceFeatureCatalog.LoadAsync(client, token);
        var identities = new List<string>();
        var models = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots.EnumerateArray())
        {
            var space = root.GetProperty("spaceId").GetString()!;
            await SendAsync(client, HttpMethod.Get,
                $"/api/workspace/spaces/{Uri.EscapeDataString(space)}/root", null, entryPosition, token);
            var contents = await ReadRootContentsAsync(client, root, entryPosition, token, features);
            if (contents.GetProperty("total").GetInt32() != contents.GetProperty("items").GetArrayLength())
                throw new InvalidOperationException("正式重启样本未在一页完整返回，不能以部分关系快照证明持久性。");
            identities.AddRange(ContentIdentities(contents).Select(identity => space + ":" + identity));
            foreach (var item in contents.GetProperty("items").EnumerateArray())
                models.Add(item.GetProperty("associatedObject").GetProperty("modelKey").GetString()!);
        }
        var samples = await ReadContentsAtPositionAsync(client, sampleFolder, entryPosition, token, features);
        if (samples.GetProperty("total").GetInt32() != samples.GetProperty("items").GetArrayLength())
            throw new InvalidOperationException("正式重启样本未在一页完整返回，不能以部分关系快照证明持久性。");
        var sampleModels = samples.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("associatedObject").GetProperty("modelKey").GetString()!).ToHashSet(StringComparer.Ordinal);
        if (!sampleModels.SetEquals(["Part", "Document"]))
            throw new InvalidOperationException("正式重启样本缺少已建立的三类型混合引用。");
        models.UnionWith(sampleModels);
        identities.AddRange(ContentIdentities(samples).Select(identity => sampleFolder.SpaceId + ":" + sampleFolder.FolderId + ":" + identity));
        if (!models.SetEquals(["WorkspaceFolder", "Part", "Document"]))
            throw new InvalidOperationException("正式重启样本缺少已建立的三类型混合引用。");
        return identities.Order(StringComparer.Ordinal).ToArray();
    }

    // 关系与模型身份均参与比对，同值 EID 跨模型及同对象不同 LID 不得合并。
    internal static string[] ContentIdentities(JsonElement contents) => contents.GetProperty("items").EnumerateArray()
        .Select(item => JsonSerializer.Serialize(new[] { item.GetProperty("linkId").GetString(),
            item.GetProperty("associatedObject").GetProperty("modelKey").GetString(), item.GetProperty("associatedObject").GetProperty("id").GetString() }))
        .Order(StringComparer.Ordinal).ToArray();

    // 根内容查询与真实列表使用相同的正式 Link 入口；语义比较只留在 API 层。
    internal static Task<JsonElement> ReadRootContentsAsync(HttpClient client, JsonElement root, string entryPosition, CancellationToken token,
        AcceptanceFeatureCatalog? features = null)
    {
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        return ReadContentsAtPositionAsync(client, new(root.GetProperty("spaceId").GetString()!, rootId, rootId, []), entryPosition, token, features);
    }

    // 普通目录与根共用完整位置和正式 Link 查询，调用者不自行拼接或省略祖先链。
    internal static async Task<JsonElement> ReadContentsAtPositionAsync(HttpClient client, WorkspaceFolderPosition position, string entryPosition, CancellationToken token,
        AcceptanceFeatureCatalog? features = null)
    {
        features ??= await AcceptanceFeatureCatalog.LoadAsync(client, token);
        var scope = JsonSerializer.Serialize(new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = JsonSerializer.Serialize(position.LinkIds) });
        return await SendAsync(client, HttpMethod.Get,
            $"/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId={Uri.EscapeDataString(position.FolderId)}&queryScope={Uri.EscapeDataString(scope)}&pageSize=20",
            null, entryPosition, token, features.ReadDeclaration("Workspace", "workspace.folder.list", entryPosition));
    }

    // 显式正向准备先解析实际按钮；原始SendAsync继续允许测试遗漏身份，不按URL补全。
    internal static Task<JsonElement> SendButtonAsync(HttpClient client, AcceptanceFeatureCatalog features, FeaturePoint point,
        HttpMethod method, string path, object? body, CancellationToken token) =>
        SendAsync(client, method, path, body, point.Position, token, buttonId: features.Button(point));

    // 正式命令保留空请求体；所有写请求取得同会话防伪令牌，Secure Cookie 仅在 loopback client 显式回放。
    internal static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path,
        object? body, string? featurePosition, CancellationToken token, string? dataDeclaration = null, string? buttonId = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions());
        if (featurePosition is not null) request.Headers.Add("X-NewP-Feature", featurePosition);
        if (dataDeclaration is not null) request.Headers.Add("X-NewP-Data-Declaration", dataDeclaration);
        if (buttonId is not null) request.Headers.Add("X-NewP-Button", buttonId);
        if (method != HttpMethod.Get)
        {
            using var antiforgeryResponse = await client.GetAsync("/api/auth/antiforgery-token", token);
            var antiforgeryText = await antiforgeryResponse.Content.ReadAsStringAsync(token);
            EnsureSuccess(antiforgeryResponse, antiforgeryText, "issue antiforgery token");
            using var antiforgery = JsonDocument.Parse(antiforgeryText);
            request.Headers.Add("X-CSRF-TOKEN", antiforgery.RootElement.GetProperty("requestToken").GetString());
        }
        using var response = await client.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        EnsureSuccess(response, text, $"{method} {path}");
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var cookie = values.Select(value => value.Split(';', 2)[0]).SingleOrDefault(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal));
            if (cookie is not null)
            {
                client.DefaultRequestHeaders.Remove("Cookie");
                client.DefaultRequestHeaders.Add("Cookie", cookie);
            }
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        using var payload = JsonDocument.Parse(text);
        return payload.RootElement.Clone();
    }

    private static string EntityId(JsonElement payload) => payload.GetProperty("fields").GetProperty("EID").GetString()
        ?? throw new InvalidOperationException("正式实体创建响应缺少 EID。");

    private static void EnsureSuccess(HttpResponseMessage response, string text, string operation)
    {
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{operation} returned {(int)response.StatusCode}: {text}");
    }
}
