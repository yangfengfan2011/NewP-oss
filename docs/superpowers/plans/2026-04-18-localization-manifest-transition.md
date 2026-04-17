# 用户可见文本多语言基础（第一包）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立宿主启动阶段产出的纯 `localization manifest` 最小闭环，并清理当前 `modules/Platform/metadata` 下的过渡 `dic/enum` 资产与 Shell 样例接线。

**Architecture:** 保持 `modules/<ModuleId>/metadata/dictionaries/*.dic.json` 与 `modules/Platform/metadata/languages.json` 作为 authoring 真源，由宿主在 `RuntimeViewMaterializer` 阶段投影出前端静态 `localization manifest`。前端 `LocalizationProvider` 只消费该 manifest，不再直接 import 模块元数据文件；同时删除当前 `Platform` 过渡 `dic/enum` 资产和首页样例展示，避免旧样例继续成为正式基线。

**Tech Stack:** .NET 10 / ASP.NET Core / C# / React / TypeScript / Vite / Vitest / xUnit / PowerShell

---

## 文件结构与职责

**Create:**

- `server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs`
  宿主运行时多语言 manifest 构建器；输入 `runtime/modules/**/metadata/dictionaries/*.dic.json` 与 `runtime/modules/Platform/metadata/languages.json`，输出纯文本 manifest JSON。
- `frontend/packages/shell/src/localization/runtimeLocalizationManifest.ts`
  前端运行时 manifest 类型定义、加载函数与标准化入口。
- `frontend/packages/shell/src/__tests__/runtime-localization-manifest.test.ts`
  前端 manifest 加载与 `LocalizationProvider` 接线的定向测试。
- `docs/verification/user-visible-text-localization-foundation-package-1-verification.md`
  第一包实施验证记录。

**Modify:**

- `server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
  在现有 runtime/public 投影链路中增加 `localization-manifest.json` 的 staging/commit 输出。
- `apps/host-api/Program.cs`
  暴露宿主运行时 `localization-manifest.json` 静态路由。
- `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`
  补 `localization-manifest.json` 产出、冲突与回滚测试。
- `tests/Kernel.Tests/HostApiPluginManifestRouteTests.cs`
  增加宿主 `localization-manifest` 路由测试。
- `frontend/packages/shell/src/App.tsx`
  改为先加载 runtime manifest，再初始化 `LocalizationProvider`。
- `frontend/packages/shell/src/pages/Home.tsx`
  删除依赖旧 `Platform` 过渡资产的语言/枚举样例区块。
- `frontend/packages/shell/src/__tests__/localization-sample.test.tsx`
  改成验证 runtime manifest 接线，而不是验证旧 `Platform` 词条。
- `frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx`
  删除或改写为不再依赖旧 `Platform` 枚举元数据。
- `docs/current-status.md`
  增补第一包收口结果。
- `docs/designs/user-visible-text-localization-foundation-design.md`
  仅在实现与设计不一致时回填最小实现落点。
- `docs/designs/README.md`
  如需要补充计划/验证链接则更新。

**Delete:**

- `frontend/packages/shell/src/localization/catalogs.ts`
- `modules/Platform/metadata/dictionaries/*.dic.json`
- `modules/Platform/metadata/enumerations/*.json`

---

### Task 1: 宿主运行时 `localization manifest` 产出

**Files:**
- Create: `server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs`
- Modify: `server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
- Test: `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`

- [ ] **Step 1: 先为 manifest 构建器补 failing tests**

```csharp
[Fact]
public void Materialize_WritesLocalizationManifest_FromPlatformLanguagesAndModuleDictionaries()
{
    var installRoot = CreateTempInstallRoot();
    var stateStore = CreateStateStoreWithEnabledPackage(
        installRoot,
        moduleId: "ConfigCenter",
        version: "1.0.0",
        dictionaries: new Dictionary<string, string>
        {
            ["Pages.dic.json"] = """
            {
              "schemaVersion": "1.0",
              "type": "dic",
              "Pages": {
                "ListTitle": {
                  "zhs": "配置中心",
                  "en": "Config Center"
                }
              }
            }
            """
        },
        platformLanguagesJson: """
        {
          "defaultLanguage": "zhs",
          "supportedLanguages": [
            { "code": "zhs", "displayName": "简体中文" },
            { "code": "en", "displayName": "English" }
          ]
        }
        """);

    var materializer = new RuntimeViewMaterializer(stateStore);
    materializer.Materialize(installRoot);

    var manifestPath = Path.Combine(installRoot, "runtime", "localization-manifest.json");
    var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

    Assert.Equal("zhs", manifest.RootElement.GetProperty("defaultLanguage").GetString());
    Assert.Equal("配置中心", manifest.RootElement.GetProperty("catalogs").GetProperty("zhs").GetProperty("Pages.ListTitle").GetString());
    Assert.Equal("Config Center", manifest.RootElement.GetProperty("catalogs").GetProperty("en").GetProperty("Pages.ListTitle").GetString());
}
```

- [ ] **Step 2: 跑定向测试，确认当前失败**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~RuntimeViewMaterializerTests -nr:false -v minimal
```

Expected:

```text
FAIL
... localization-manifest.json not found / missing test helper implementation ...
```

- [ ] **Step 3: 实现 `LocalizationRuntimeManifestBuilder`**

```csharp
using System.Text.Json;

namespace ICP.Platform.Kernel.PluginPackages;

public sealed class LocalizationRuntimeManifestBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string Build(string runtimeRoot)
    {
        var modulesRoot = Path.Combine(runtimeRoot, "modules");
        var languagesPath = Path.Combine(modulesRoot, "Platform", "metadata", "languages.json");
        if (!File.Exists(languagesPath))
        {
            return JsonSerializer.Serialize(new
            {
                defaultLanguage = "zhs",
                supportedLanguages = Array.Empty<object>(),
                catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            }, JsonOptions) + Environment.NewLine;
        }

        var languages = JsonSerializer.Deserialize<LocalizationLanguagesDocument>(File.ReadAllText(languagesPath), JsonOptions)
            ?? throw new JsonException($"Localization languages document '{languagesPath}' deserialized to null.");

        var catalogs = languages.SupportedLanguages
            .ToDictionary(
                language => language.Code,
                _ => new Dictionary<string, string>(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);

        foreach (var dicPath in Directory.GetFiles(modulesRoot, "*.dic.json", SearchOption.AllDirectories))
        {
            var document = JsonSerializer.Deserialize<DictionaryDocument>(File.ReadAllText(dicPath), JsonOptions)
                ?? throw new JsonException($"Dictionary document '{dicPath}' deserialized to null.");
            document.ApplyTo(catalogs, dicPath);
        }

        return JsonSerializer.Serialize(new
        {
            defaultLanguage = languages.DefaultLanguage,
            supportedLanguages = languages.SupportedLanguages.Select(language => new
            {
                code = language.Code,
                displayName = language.DisplayName
            }),
            catalogs
        }, JsonOptions) + Environment.NewLine;
    }

    private sealed class LocalizationLanguagesDocument
    {
        public string DefaultLanguage { get; set; } = "zhs";
        public List<LocalizationLanguage> SupportedLanguages { get; set; } = [];
    }

    private sealed class LocalizationLanguage
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 4: 在 `RuntimeViewMaterializer` 中写出 manifest**

```csharp
private readonly LocalizationRuntimeManifestBuilder _localizationManifestBuilder;

public RuntimeViewMaterializer(InstalledPluginStateStore stateStore, FrontendRuntimeManifestBuilder frontendManifestBuilder)
    : this(stateStore, frontendManifestBuilder, new LocalizationRuntimeManifestBuilder(), Directory.Move)
{
}

var localizationManifestPath = Path.Combine(stagedRuntimeRoot, "localization-manifest.json");
File.WriteAllText(localizationManifestPath, _localizationManifestBuilder.Build(stagedRuntimeRoot));
```

- [ ] **Step 5: 继续补冲突与回滚测试**

```csharp
[Fact]
public void Materialize_Throws_WhenDictionaryEntryMissesSupportedLanguage()
{
    // 构造 zhs/en 双语配置，但 dic 只提供 zhs，断言 Materialize 抛 InvalidOperationException。
}

[Fact]
public void Materialize_RestoresPreviousLocalizationManifest_WhenSwapFailsAfterRuntimeCommit()
{
    // 先生成 baseline manifest，再模拟 public swap 失败，断言 runtime/localization-manifest.json 保持上一版内容。
}
```

- [ ] **Step 6: 重新运行 kernel 定向测试，确认通过**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~HostApiPluginManifestRouteTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 7: 提交**

```bash
git add server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs server/kernel/PluginPackages/RuntimeViewMaterializer.cs tests/Kernel.Tests/RuntimeViewMaterializerTests.cs tests/Kernel.Tests/HostApiPluginManifestRouteTests.cs
git commit -m "功能：增加运行时多语言 manifest 投影"
```

### Task 2: 宿主路由与前端 `LocalizationProvider` 接线切换

**Files:**
- Create: `frontend/packages/shell/src/localization/runtimeLocalizationManifest.ts`
- Modify: `apps/host-api/Program.cs`
- Modify: `frontend/packages/shell/src/App.tsx`
- Test: `frontend/packages/shell/src/__tests__/runtime-localization-manifest.test.ts`

- [ ] **Step 1: 先为宿主路由和前端加载补 failing tests**

```csharp
[Fact]
public async Task LocalizationManifestRoute_ReturnsJson()
{
    var response = await client.GetAsync("/plugins/localization-manifest.json");
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.True(document.RootElement.TryGetProperty("catalogs", out _));
}
```

```ts
it('loads runtime localization manifest before rendering LocalizationProvider', async () => {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
    defaultLanguage: 'zhs',
    supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }],
    catalogs: { zhs: { 'Pages.ListTitle': '配置中心' } }
  })));

  const manifest = await loadRuntimeLocalizationManifest();
  expect(manifest.catalogs.zhs['Pages.ListTitle']).toBe('配置中心');
});
```

- [ ] **Step 2: 跑前端定向测试，确认当前失败**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/runtime-localization-manifest.test.ts src/__tests__/localization-sample.test.tsx
```

Expected:

```text
FAIL
... loadRuntimeLocalizationManifest is not defined ...
```

- [ ] **Step 3: 在宿主新增静态 manifest 路由**

```csharp
app.Map("/plugins/localization-manifest.json", manifestApp =>
{
    manifestApp.Run(async context =>
    {
        var manifestPath = Path.Combine(pluginInstallRoot, "runtime", "localization-manifest.json");
        if (!File.Exists(manifestPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.SendFileAsync(manifestPath);
    });
});
```

- [ ] **Step 4: 实现前端 runtime manifest 加载器**

```ts
export type RuntimeLocalizationManifest = {
  defaultLanguage: string;
  supportedLanguages: Array<{ code: string; displayName: string }>;
  catalogs: Record<string, Record<string, string>>;
};

export async function loadRuntimeLocalizationManifest(): Promise<RuntimeLocalizationManifest> {
  const response = await fetch('/plugins/localization-manifest.json', { credentials: 'same-origin' });
  if (!response.ok) {
    throw new Error(`Failed to load localization manifest: ${response.status}`);
  }

  return response.json() as Promise<RuntimeLocalizationManifest>;
}
```

- [ ] **Step 5: 修改 `App.tsx` 先加载 manifest 再挂 `LocalizationProvider`**

```tsx
const [manifest, setManifest] = useState<RuntimeLocalizationManifest | null>(null);

useEffect(() => {
  loadRuntimeLocalizationManifest().then(setManifest);
}, []);

if (!manifest) {
  return null;
}

<LocalizationProvider
  defaultLanguage={manifest.defaultLanguage}
  supportedLanguages={manifest.supportedLanguages}
  catalogs={manifest.catalogs}
>
```

- [ ] **Step 6: 重新跑宿主路由与前端测试**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~HostApiPluginManifestRouteTests -nr:false -v minimal
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/runtime-localization-manifest.test.ts src/__tests__/localization-sample.test.tsx
```

Expected:

```text
Passed!
```

- [ ] **Step 7: 提交**

```bash
git add apps/host-api/Program.cs frontend/packages/shell/src/App.tsx frontend/packages/shell/src/localization/runtimeLocalizationManifest.ts frontend/packages/shell/src/__tests__/runtime-localization-manifest.test.ts frontend/packages/shell/src/__tests__/localization-sample.test.tsx
git commit -m "功能：切换前端多语言到运行时 manifest"
```

### Task 3: 清理 `Platform` 过渡资产与 Shell 样例链路

**Files:**
- Delete: `modules/Platform/metadata/dictionaries/*.dic.json`
- Delete: `modules/Platform/metadata/enumerations/*.json`
- Delete: `frontend/packages/shell/src/localization/catalogs.ts`
- Modify: `frontend/packages/shell/src/pages/Home.tsx`
- Modify: `frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx`

- [ ] **Step 1: 先为首页样例区块下线写 failing test**

```ts
it('does not render legacy localization sample panels after localization manifest migration', async () => {
  renderHome();
  expect(screen.queryByText('语言资源样例')).toBeNull();
  expect(screen.queryByText('枚举资源样例')).toBeNull();
});
```

- [ ] **Step 2: 运行定向测试，确认当前失败**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx
```

Expected:

```text
FAIL
... found text "语言资源样例" ...
```

- [ ] **Step 3: 删除旧 `Platform` 过渡资产和 `catalogs.ts`**

```powershell
Remove-Item -LiteralPath .\frontend\packages\shell\src\localization\catalogs.ts
Remove-Item -LiteralPath .\modules\Platform\metadata\dictionaries -Recurse
Remove-Item -LiteralPath .\modules\Platform\metadata\enumerations -Recurse
```

- [ ] **Step 4: 精简 `Home.tsx`，移除旧样例区块**

```tsx
// 删除 useLocalization/useEnumeration、删除“语言资源样例”“枚举资源样例”两个 section，
// 保留门户入口、最近访问、常用操作等与多语言过渡资产无关的内容。
```

- [ ] **Step 5: 删除或改写依赖旧样例资产的测试**

```ts
describe('Home localization sample', () => {
  it('renders portal panels without legacy localization sample sections', async () => {
    render(<App />);
    expect(screen.queryByText('语言资源样例')).toBeNull();
    expect(screen.queryByText('枚举资源样例')).toBeNull();
  });
});
```

- [ ] **Step 6: 跑前端构建与测试，确认清理后稳定**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/runtime-localization-manifest.test.ts src/__tests__/localization-sample.test.tsx
pnpm --dir .\frontend build
```

Expected:

```text
Passed!
vite build completed successfully
```

- [ ] **Step 7: 提交**

```bash
git add frontend/packages/shell/src/App.tsx frontend/packages/shell/src/pages/Home.tsx frontend/packages/shell/src/__tests__/localization-sample.test.tsx frontend/packages/shell/src/__tests__/runtime-localization-manifest.test.ts
git add -u frontend/packages/shell/src/localization/catalogs.ts modules/Platform/metadata/dictionaries modules/Platform/metadata/enumerations frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx
git commit -m "重构：清理平台多语言过渡样例资产"
```

### Task 4: 文档、验证记录与状态收口

**Files:**
- Create: `docs/verification/user-visible-text-localization-foundation-package-1-verification.md`
- Modify: `docs/current-status.md`
- Modify: `docs/designs/user-visible-text-localization-foundation-design.md`

- [ ] **Step 1: 先写验证文档骨架**

```md
# 用户可见文本多语言基础第一包验证

## 范围

- 宿主启动阶段 `localization manifest`
- 前端 `LocalizationProvider` runtime manifest 接线
- `Platform` 过渡 `dic/enum` 资产清理
```

- [ ] **Step 2: 记录真实命令与结果**

```md
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~HostApiPluginManifestRouteTests" -nr:false -v minimal`
- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/runtime-localization-manifest.test.ts src/__tests__/localization-sample.test.tsx`
- `pnpm --dir .\frontend build`
```

- [ ] **Step 3: 更新 `docs/current-status.md` 的相关小节**

```md
- 当前前端多语言已切换为宿主启动阶段产出的纯 `localization manifest`
- `modules/Platform/metadata/dictionaries/*.dic.json` 与 `enumerations/*.json` 已清理
- `modules/Platform/metadata/languages.json` 保留为全局语言配置入口
```

- [ ] **Step 4: 如实现与设计补充点一致，最小回填设计文档**

```md
- 运行时 `localization manifest` 最小字段已落地为 `defaultLanguage`、`supportedLanguages`、`catalogs`
- 前端 `LocalizationProvider` 已切换为只消费宿主产出的 manifest
```

- [ ] **Step 5: 运行最小验证命令并确认文档可对齐**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\user-visible-text-localization-foundation-design.md
git diff -- .\docs\current-status.md .\docs\verification\user-visible-text-localization-foundation-package-1-verification.md
```

Expected:

```text
AI-first design validation passed
diff only shows this package's intended doc updates
```

- [ ] **Step 6: 提交**

```bash
git add docs/current-status.md docs/designs/user-visible-text-localization-foundation-design.md docs/verification/user-visible-text-localization-foundation-package-1-verification.md
git commit -m "验证：收口多语言基础第一包文档"
```

## Self-Review

- **Spec coverage:** 本计划只覆盖设计文档中的第一包：`localization manifest` 投影链路、`Platform` 过渡资产清理、Shell 接线切换、验证与状态收口。后端消息契约 DTO 落地未纳入本计划，需要下一份独立计划承接。
- **Placeholder scan:** 已去除 `TODO/TBD/待补` 等占位词，所有步骤均给出明确文件、命令和最小代码片段。
- **Type consistency:** 计划统一使用 `localization manifest`、`catalogs`、`messageKey`、`fieldKey` 这些术语，与设计文档当前口径一致。

