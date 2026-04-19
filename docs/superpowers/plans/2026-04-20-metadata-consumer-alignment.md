# 元数据消费链对齐实施计划（`.dic / .model / .settings`）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `.dic / .model / .settings` 三条已落地的元数据消费链统一收口到“按文件头识别类型、按业务根对象提供语义、按显式文件集合与模块顺序决定生效结果”的实现口径。

**Architecture:** 本轮不重做 authoring 布局，也不扩展到 `nav / view / enum`。实现上分三条线并行收口：`.dic` 继续输出前端扁平 `catalogs`，但 builder 不再依赖固定目录与文件名推导；`.model` 把工具侧单业务根对象 loader 提升为共享消费能力，并去掉对 `metadata/models` 的硬绑定；`.settings` 保留 manifest 扫描层，但让 authoring/apply 服务改为消费精确 `SourceFiles`，同时补上 `type = "settings"` 的硬门禁。

**Tech Stack:** .NET 10 / C# / xUnit / ASP.NET Core / PowerShell

---

## 范围说明

本计划取代仅覆盖 `.dic` 的窄计划，作为当前真正进入实施前的统一计划。后续若按本计划进入实现，应优先以本文件为准。

## 文件结构与职责

**Create:**

- `tests/Kernel.Tests/LocalizationRuntimeManifestBuilderTests.cs`  
  `.dic` builder 的定向测试。
- `tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`  
  `ConfigCenter` 模型消费链的定向测试。
- `tests/DevSchemaSync.Tests/ModelDiscoveryServiceTests.cs`  
  工具侧模型发现与 loader 的定向测试。
- `tests/MetadataConfigCenter.Tests/SettingDefinitionSourceFilesTests.cs`  
  `settings` 精确文件集读取与 apply 的定向测试。

**Modify:**

- `server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs`
- `server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
- `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- `server/platform-modeling/metadata/IModelMetadataDocumentLoader.cs`
- `server/platform-modeling/metadata/JsonModelMetadataDocumentLoader.cs`
- `server/tools/dev-schema-sync/Models/ModelDiscoveryService.cs`
- `server/tools/dev-schema-sync/Models/SingleRootModelDocumentLoader.cs`
- `server/modules/MetadataConfigCenter/Services/SettingDefinitionAuthoringService.cs`
- `server/modules/MetadataConfigCenter/Services/SettingDefinitionApplyService.cs`
- `server/modules/MetadataConfigCenter/Adapters/SettingDefinitionAuthoringAdapter.cs`
- `server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs`
- `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`
- `tests/Kernel.Tests/HostApiPluginManifestRouteTests.cs`
- `docs/designs/platform/metadata-consumer-alignment-review.md`

---

### Task 1：先用测试冻结三条链的目标行为

**Files:**
- Create: `tests/Kernel.Tests/LocalizationRuntimeManifestBuilderTests.cs`
- Create: `tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`
- Create: `tests/DevSchemaSync.Tests/ModelDiscoveryServiceTests.cs`
- Create: `tests/MetadataConfigCenter.Tests/SettingDefinitionSourceFilesTests.cs`

- [x] **Step 1: 为 `.dic` 写第一个失败测试，证明非 `dictionaries/` 目录下的 dic 也必须被识别**

```csharp
[Fact]
public void Build_WhenDicFileLivesOutsideDictionariesDirectory_StillProjectsCatalog()
{
    var runtimeRoot = CreateRuntimeRoot();
    WriteLanguages(runtimeRoot);
    WriteMetadataFile(
        runtimeRoot,
        "Audit",
        Path.Combine("presentation", "PersonListView.dic.json"),
        """
        {
          "schemaVersion": "1.0",
          "type": "dic",
          "PersonListView": {
            "Title": {
              "zhs": "人员中心",
              "en": "People Center"
            }
          }
        }
        """);

    var builder = new LocalizationRuntimeManifestBuilder();
    var manifest = builder.Build(runtimeRoot, ["Platform", "Audit"]);

    using var document = JsonDocument.Parse(manifest);
    Assert.Equal("人员中心", document.RootElement.GetProperty("catalogs").GetProperty("zhs").GetProperty("PersonListView.Title").GetString());
}
```

- [x] **Step 2: 为 `.model` 写第一个失败测试，证明 loader 不应要求 `metadata/models` 固定目录**

```csharp
[Fact]
public void DiscoverAll_WhenModelLivesOutsideModelsDirectory_StillFindsModel()
{
    var repositoryRoot = CreateRepositoryRoot();
    WriteModelFile(
        repositoryRoot,
        "ConfigCenter",
        Path.Combine("domain", "ConfigItem.model.json"),
        """
        {
          "schemaVersion": "1.0",
          "type": "model",
          "ConfigItem": {
            "kind": "Entity",
            "fields": []
          }
        }
        """);

    var service = new ModelDiscoveryService(new SingleRootModelDocumentLoader());
    var discovered = service.DiscoverAll(repositoryRoot);

    Assert.Contains(discovered, item => item.ModuleId == "ConfigCenter" && item.ModelName == "ConfigItem");
}
```

- [x] **Step 3: 为 `.settings` 写第一个失败测试，证明 authoring 服务应按 `SourceFiles` 而不是单 `SourcePath` 读取**

```csharp
[Fact]
public async Task GetSettingDefinitionsAsync_WhenSourceFilesSpanMultipleDirectories_ReadsExactFiles()
{
    var service = CreateAuthoringServiceWithManifest(
        moduleId: "ConfigCenter",
        sourcePath: "settings",
        sourceFiles:
        [
            Path.Combine("settings", "Base.settings.json"),
            Path.Combine("domain", "Advanced.settings.json")
        ]);

    var definitions = await service.GetSettingDefinitionsAsync("ConfigCenter");

    Assert.Contains(definitions, item => item.Key == "ConfigCenter.BaseEnabled");
    Assert.Contains(definitions, item => item.Key == "ConfigCenter.AdvancedEnabled");
}
```

- [x] **Step 4: 跑三组定向测试，确认当前失败**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~LocalizationRuntimeManifestBuilderTests -nr:false -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter FullyQualifiedName~ConfigCenterModelMetadataProviderTests -nr:false -v minimal
dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj --filter FullyQualifiedName~SettingDefinitionSourceFilesTests -nr:false -v minimal
```

Expected:

```text
FAIL
```

- [x] **Step 5: 提交**

```bash
git add tests/Kernel.Tests/LocalizationRuntimeManifestBuilderTests.cs tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs tests/DevSchemaSync.Tests/ModelDiscoveryServiceTests.cs tests/MetadataConfigCenter.Tests/SettingDefinitionSourceFilesTests.cs
git commit -m "测试：冻结元数据消费链对齐目标行为"
```

### Task 2：收口 `.dic` 消费链

**Files:**
- Modify: `server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs`
- Modify: `server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
- Modify: `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`

- [x] **Step 1: 先把 builder 改为显式接收模块顺序**

```csharp
public string Build(string runtimeRoot, IReadOnlyList<string> moduleOrder)
{
    // 后续扫描与覆盖都以 moduleOrder 为准，不再让目录枚举顺序形成生效语义。
}
```

- [x] **Step 2: 改为扫描 `modules/<ModuleId>/metadata/**/*.json`，并用 `type = "dic"` 识别**

```csharp
foreach (var moduleId in moduleOrder)
{
    foreach (var filePath in EnumerateMetadataJsonFiles(runtimeRoot, moduleId))
    {
        using var document = JsonDocument.Parse(File.ReadAllText(filePath));
        if (!IsDicDocument(document.RootElement, filePath))
        {
            continue;
        }

        ApplyDictionaryDocument(filePath, document.RootElement, supportedLanguages, catalogs);
    }
}
```

- [x] **Step 3: 用单业务根对象确定 `dicKey`，文件名只做一致性校验**

```csharp
var dicRoot = GetSingleBusinessRoot(root, dicPath);
var dicKey = dicRoot.Name;
var fileStem = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(dicPath));
if (!string.Equals(fileStem, dicKey, StringComparison.Ordinal))
{
    throw new JsonException($"Dictionary '{dicPath}' file name '{fileStem}' does not match business root '{dicKey}'.");
}
```

- [x] **Step 4: 把冲突规则改为 `dic-entry` 覆盖**

```csharp
foreach (var translation in translations)
{
    catalogs[translation.Key][$"{dicKey}.{entry.Name}"] = translation.Value;
}
```

- [x] **Step 5: 在 `RuntimeViewMaterializer` 中传入已启用模块顺序**

```csharp
var enabledModuleOrder = enabledRecords
    .Select(record => record.ModuleId)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

File.WriteAllText(
    localizationManifestPath,
    _localizationManifestBuilder.Build(stagedRuntimeRoot, enabledModuleOrder));
```

- [x] **Step 6: 重跑 `.dic` 定向测试与 runtime 路由 smoke**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~LocalizationRuntimeManifestBuilderTests|FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~HostApiPluginManifestRouteTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 7: 提交**

```bash
git add server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs server/kernel/PluginPackages/RuntimeViewMaterializer.cs tests/Kernel.Tests/LocalizationRuntimeManifestBuilderTests.cs tests/Kernel.Tests/RuntimeViewMaterializerTests.cs tests/Kernel.Tests/HostApiPluginManifestRouteTests.cs
git commit -m "功能：收口 dic 消费链到自描述规则"
```

### Task 3：收口 `.model` 消费链

**Files:**
- Modify: `server/platform-modeling/metadata/IModelMetadataDocumentLoader.cs`
- Modify: `server/platform-modeling/metadata/JsonModelMetadataDocumentLoader.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- Modify: `server/tools/dev-schema-sync/Models/ModelDiscoveryService.cs`
- Modify: `server/tools/dev-schema-sync/Models/SingleRootModelDocumentLoader.cs`

- [x] **Step 1: 把单业务根对象解析规则收口到共享 model loader**

```csharp
public interface IModelMetadataDocumentLoader
{
    ModelMetadataDocument LoadFromFile(string path);
}

public sealed class JsonModelMetadataDocumentLoader : IModelMetadataDocumentLoader
{
    public ModelMetadataDocument LoadFromFile(string path)
    {
        // 校验 schemaVersion/type，读取唯一业务根对象，回填 Model.Name 与 ModuleId。
    }
}
```

- [x] **Step 2: 删除工具侧对 `metadata/models` 的硬依赖**

```csharp
private static string ExtractModuleId(string path)
{
    var segments = Path.GetFullPath(path)
        .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    for (var index = 0; index < segments.Length - 2; index++)
    {
        if (string.Equals(segments[index], "modules", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[index + 2], "metadata", StringComparison.OrdinalIgnoreCase))
        {
            return segments[index + 1];
        }
    }

    throw new InvalidOperationException($"无法从路径提取 moduleId: {path}");
}
```

- [x] **Step 3: `ModelDiscoveryService` 仍可先保留 `*.model.json` 扫描，但不再要求固定目录名**

```csharp
var discovered = Directory.EnumerateFiles(modulesRoot, "*.model.json", SearchOption.AllDirectories)
    .Select(path => loader.LoadFromFile(path))
    .ToArray();
```

- [x] **Step 4: `ConfigCenterModelMetadataProvider` 不再内联重复解析逻辑**

```csharp
private readonly IModelMetadataDocumentLoader _modelLoader = new JsonModelMetadataDocumentLoader();

public ModelDefinition LoadConfigItem()
{
    var document = _modelLoader.LoadFromFile(ConfigItemPath);
    var typeMap = _typeMapLoader.LoadFromFile(StorageTypeMapPath);
    _validator.Validate(document, typeMap);
    return _projector.Project(document, typeMap, "SqlServer");
}
```

- [x] **Step 5: 重跑 `.model` 定向测试**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter FullyQualifiedName~ConfigCenterModelMetadataProviderTests -nr:false -v minimal
dotnet test .\tests\DevSchemaSync.Tests\DevSchemaSync.Tests.csproj --filter FullyQualifiedName~ModelDiscoveryServiceTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 6: 提交**

```bash
git add server/platform-modeling/metadata/IModelMetadataDocumentLoader.cs server/platform-modeling/metadata/JsonModelMetadataDocumentLoader.cs server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs server/tools/dev-schema-sync/Models/ModelDiscoveryService.cs server/tools/dev-schema-sync/Models/SingleRootModelDocumentLoader.cs tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs tests/DevSchemaSync.Tests/ModelDiscoveryServiceTests.cs
git commit -m "功能：收口 model 消费链到单根对象规则"
```

### Task 4：收口 `.settings` 消费链

**Files:**
- Modify: `server/modules/MetadataConfigCenter/Services/SettingDefinitionAuthoringService.cs`
- Modify: `server/modules/MetadataConfigCenter/Services/SettingDefinitionApplyService.cs`
- Modify: `server/modules/MetadataConfigCenter/Adapters/SettingDefinitionAuthoringAdapter.cs`
- Modify: `server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs`
- Modify: `tests/MetadataConfigCenter.Tests/SettingDefinitionSourceFilesTests.cs`

- [x] **Step 1: 在 adapter 中把 `type = "settings"` 补成硬门禁**

```csharp
if (!root.TryGetProperty("type", out var typeElement)
    || typeElement.ValueKind != JsonValueKind.String
    || !string.Equals(typeElement.GetString(), "settings", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Settings metadata file '{sourceFile}' must declare type = 'settings'.");
}
```

- [x] **Step 2: authoring 服务改为按 `SourceFiles` 精确读取**

```csharp
var sourceFiles = await GetSettingsSourceFilesAsync(moduleId, cancellationToken);
foreach (var relativeFilePath in sourceFiles)
{
    var filePath = _moduleMetadataStore.ResolveMetadataRelativeFilePath(moduleId, relativeFilePath);
    var content = await _moduleMetadataStore.ReadTextAsync(filePath, cancellationToken);
    ...
}
```

- [x] **Step 3: `ModuleMetadataStore` 增加“按 metadata 根相对路径解析文件”的方法**

```csharp
public string ResolveMetadataRelativeFilePath(string moduleId, string relativeFilePath)
{
    var effectiveRoot = ResolvePreferredMetadataRoot(moduleId, string.Empty) ?? _repositoryRoot;
    return Path.Combine(effectiveRoot, "modules", moduleId, "metadata", relativeFilePath);
}

public string ResolveAuthoringMetadataRelativeFilePath(string moduleId, string relativeFilePath)
{
    var effectiveRoot = ResolvePreferredAuthoringRoot(moduleId, string.Empty);
    return Path.Combine(effectiveRoot, "modules", moduleId, "metadata", relativeFilePath);
}
```

- [x] **Step 4: apply 服务改为按精确文件集合投影，而不是只按单目录整体替换**

```csharp
var sourceFiles = await GetSettingsSourceFilesAsync(moduleId, cancellationToken);
foreach (var relativeFilePath in sourceFiles)
{
    var sourceFilePath = _moduleMetadataStore.ResolveAuthoringMetadataRelativeFilePath(moduleId, relativeFilePath);
    var runtimeFilePath = _moduleMetadataStore.ResolveRuntimeMetadataRelativeFilePath(moduleId, relativeFilePath);
    await CopyFileAtomicallyAsync(sourceFilePath, runtimeFilePath, cancellationToken);
}
```

- [x] **Step 5: 如需兼容旧状态，仍可在单目录场景下复用现有目录级比较，但精确文件集必须优先**

```csharp
// 先按 SourceFiles 比较和投影；只有旧测试数据仍是单目录时，目录级逻辑才作为兼容包装层存在。
```

- [x] **Step 6: 重跑 `settings` 定向测试**

Run:

```powershell
dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj --filter FullyQualifiedName~SettingDefinitionSourceFilesTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 7: 提交**

```bash
git add server/modules/MetadataConfigCenter/Services/SettingDefinitionAuthoringService.cs server/modules/MetadataConfigCenter/Services/SettingDefinitionApplyService.cs server/modules/MetadataConfigCenter/Adapters/SettingDefinitionAuthoringAdapter.cs server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs tests/MetadataConfigCenter.Tests/SettingDefinitionSourceFilesTests.cs
git commit -m "功能：收口 settings 消费链到精确文件集合"
```

### Task 5：联调验证与文档回填

**Files:**
- Modify: `docs/designs/platform/metadata-consumer-alignment-review.md`

- [x] **Step 1: 跑三条链的联调命令**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~LocalizationRuntimeManifestBuilderTests|FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~HostApiPluginManifestRouteTests" -nr:false -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter FullyQualifiedName~ConfigCenterModelMetadataProviderTests -nr:false -v minimal
dotnet test .\tests\DevSchemaSync.Tests\DevSchemaSync.Tests.csproj --filter FullyQualifiedName~ModelDiscoveryServiceTests -nr:false -v minimal
dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj --filter FullyQualifiedName~SettingDefinitionSourceFilesTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 2: 如实现中边界有调整，最小回填 review 文档**

```md
- `.dic` 已改为按模块顺序 + type 门禁 + 业务根对象生成 manifest
- `.model` 已去掉对 `metadata/models` 的硬依赖
- `.settings` 已改为按精确 `SourceFiles` 读取与 apply
```

- [x] **Step 3: 对 review 文档执行 AI-first 校验**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\platform\metadata-consumer-alignment-review.md
```

Expected:

```text
AI-first design validation passed
```

- [x] **Step 4: 提交**

```bash
git add docs/designs/platform/metadata-consumer-alignment-review.md
git commit -m "验证：收口元数据消费链对齐文档"
```

## Self-Review

- **Spec coverage:** 本计划覆盖了本轮 review 中确认存在同类问题的三条已实现链路：`.dic / .model / .settings`。`nav / view / enum` 未纳入，是有意保持范围收紧。
- **Placeholder scan:** 各任务都给出了明确文件、目标行为和最小命令，没有保留 `TODO`、`待定` 或“参考上一任务”式占位。
- **Type consistency:** 全文统一使用 `type 门禁`、`业务根对象`、`模块顺序`、`SourceFiles`、`精确文件集合` 这些术语，与 review 文档口径一致。
