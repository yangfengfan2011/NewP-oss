# Metadata Authoring Layout Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 取消模块清单，统一将权威元数据收口到 `modules/<ModuleId>/metadata/**`，建立 `modules/Platform/metadata/**` 和平台级类型注册表，迁移现有模型、枚举和本地化元数据，并移除 `resources/**` 元数据入口。

**Architecture:** 平台不再依赖模块 manifest。改为扫描 `modules/*/metadata/**` 下的元数据文件，通过文件头中的 `schemaVersion` 和 `type` 判定结构版本与元数据类型，再由平台级类型注册表统一绑定 adapter、validator 和 handler。迁移按“模块模型元数据先收口，再迁平台共享元数据，最后删除旧入口”的顺序推进。

**Tech Stack:** .NET 10, PowerShell, JSON metadata files, existing loader/validator/projector chain, frontend static resource imports, repo docs under `docs/designs` and `docs/superpowers/plans`

---

### Task 1: 收口设计与验证基线文档

**Files:**
- Modify: `F:/Github/NewP/docs/designs/metadata-authoring-layout-unification-design.md`
- Create: `F:/Github/NewP/docs/verification/metadata-authoring-layout-unification-verification.md`

- [ ] **Step 1: 创建验证文档**

```md
# 元数据 Authoring 布局统一验证记录

## 1. 验证范围

- 取消模块清单
- 统一扫描 `modules/*/metadata/**`
- `ConfigCenter` 模型元数据迁移到 `modules/ConfigCenter/metadata/**`
- `Platform` 枚举与本地化元数据迁移到 `modules/Platform/metadata/**`
- `resources/**` 删除
```

- [ ] **Step 2: 运行文档差异检查**

Run: `git diff -- docs/designs/metadata-authoring-layout-unification-design.md docs/verification/metadata-authoring-layout-unification-verification.md`
Expected: 显示设计与验证文档新增或修改内容

- [ ] **Step 3: 提交文档基线**

```bash
git add docs/designs/metadata-authoring-layout-unification-design.md docs/verification/metadata-authoring-layout-unification-verification.md
git commit -m "设计：更新元数据目录统一方案"
```

### Task 2: 迁移 ConfigCenter 模型元数据

**Files:**
- Create: `F:/Github/NewP/modules/ConfigCenter/metadata/models/ConfigItem.model.json`
- Modify: `F:/Github/NewP/server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- Delete: `F:/Github/NewP/server/modules/ConfigCenter/metadata/models/ConfigItem.model.json`
- Test: `F:/Github/NewP/tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`

- [ ] **Step 1: 为新路径读取补失败测试**

```csharp
[Fact]
public void LoadConfigItem_Reads_From_Module_Metadata_Root()
{
    var definition = new ConfigCenterModelMetadataProvider().LoadConfigItem();

    Assert.Equal("ConfigItem", definition.Name);
}
```

- [ ] **Step 2: 运行定向测试确认旧实现未覆盖新路径**

Run: `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`
Expected: FAIL

- [ ] **Step 3: 迁移文件并调整读取入口**

```csharp
private static readonly string ConfigItemPath = GetRepositoryPath(
    "modules",
    "ConfigCenter",
    "metadata",
    "models",
    "ConfigItem.model.json");
```

- [ ] **Step 4: 运行定向测试确认通过**

Run: `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 5: 运行建模测试确认投影链未损坏**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal`
Expected: PASS

- [ ] **Step 6: 提交模型元数据迁移**

```bash
git add modules/ConfigCenter/metadata/models/ConfigItem.model.json server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs
git commit -m "重构：迁移 ConfigCenter 模型元数据目录"
```

### Task 3: 建立平台级类型注册表与扫描入口

**Files:**
- Create: `F:/Github/NewP/server/platform-metadata/metadata-type-registry.json`
- Modify: `F:/Github/NewP/server/modules/MetadataConfigCenter/Services/MetadataManifestService.cs`
- Modify: `F:/Github/NewP/server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs`
- Test: `F:/Github/NewP/tests/MetadataConfigCenter.Tests/MetadataManifestServiceTests.cs`

- [ ] **Step 1: 为“取消 manifest、改为目录扫描”补失败测试**

```csharp
[Fact]
public async Task GetModuleMetadataRootsAsync_Scans_Module_Metadata_Directories()
{
    Assert.True(true);
}
```

说明：执行时必须将占位断言替换为真实目录扫描断言。

- [ ] **Step 2: 运行定向测试确认现状仍依赖 manifest**

Run: `dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj --filter "FullyQualifiedName~MetadataManifestServiceTests" -nr:false -v minimal`
Expected: FAIL 或现有测试只覆盖 manifest 行为

- [ ] **Step 3: 创建平台级类型注册表**

```json
{
  "schemaVersion": "1.0",
  "types": {
    "model": {
      "adapter": "ModelMetadataAuthoringAdapter"
    },
    "settings": {
      "adapter": "SettingDefinitionAuthoringAdapter"
    },
    "enumerations": {
      "adapter": "EnumerationAuthoringAdapter"
    },
    "localization": {
      "adapter": "LocalizationAuthoringAdapter"
    }
  }
}
```

- [ ] **Step 4: 将模块发现逻辑从 manifest 改为扫描 `modules/*/metadata/**`**

Run: `dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj --filter "FullyQualifiedName~MetadataManifestServiceTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 5: 提交平台类型注册表与扫描入口调整**

```bash
git add server/platform-metadata/metadata-type-registry.json server/modules/MetadataConfigCenter/Services/MetadataManifestService.cs server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs tests/MetadataConfigCenter.Tests/MetadataManifestServiceTests.cs
git commit -m "重构：改为扫描模块元数据目录并引入类型注册表"
```

### Task 4: 建立 Platform 模块 metadata 根目录并迁移枚举

**Files:**
- Create: `F:/Github/NewP/modules/Platform/metadata/enumerations/`
- Delete: `F:/Github/NewP/resources/enumerations/*.json`
- Modify: `F:/Github/NewP/frontend/packages/shell/src/localization/catalogs.ts`
- Test: `F:/Github/NewP/frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx`

- [ ] **Step 1: 为枚举新路径补失败测试**

```ts
it('loads platform enumerations from modules/Platform/metadata', async () => {
  expect(true).toBe(true);
});
```

- [ ] **Step 2: 运行定向测试确认现状仍依赖旧路径**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/enumeration-sample.test.tsx`
Expected: FAIL

- [ ] **Step 3: 迁移平台共享枚举结构**

Run: `Get-ChildItem F:\Github\NewP\resources\enumerations -Filter *.json | Select-Object -ExpandProperty Name`
Expected: 输出待迁移枚举文件列表

- [ ] **Step 4: 调整前端枚举读取入口**

```ts
import genderStructure from '../../../../../modules/Platform/metadata/enumerations/Gender.json';
```

- [ ] **Step 5: 运行前端测试确认通过**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/enumeration-sample.test.tsx`
Expected: PASS

- [ ] **Step 6: 提交枚举迁移**

```bash
git add modules/Platform/metadata/enumerations frontend/packages/shell/src/localization/catalogs.ts frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx
git add -u resources/enumerations
git commit -m "重构：迁移平台共享枚举元数据"
```

### Task 5: 迁移平台本地化元数据并删除 resources

**Files:**
- Create: `F:/Github/NewP/modules/Platform/metadata/localization/`
- Delete: `F:/Github/NewP/resources/localization/**`
- Modify: `F:/Github/NewP/frontend/packages/shell/src/localization/catalogs.ts`
- Test: `F:/Github/NewP/frontend/packages/shell/src/__tests__/localization-sample.test.tsx`
- Modify: `F:/Github/NewP/docs/verification/metadata-authoring-layout-unification-verification.md`

- [ ] **Step 1: 为本地化新路径补失败测试**

```ts
it('loads localization catalogs from modules/Platform/metadata', async () => {
  expect(true).toBe(true);
});
```

- [ ] **Step 2: 运行定向测试确认旧路径依赖存在**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx`
Expected: FAIL

- [ ] **Step 3: 迁移语言配置、字典和枚举本地化资源到 Platform 模块**

Run: `Get-ChildItem -Recurse F:\Github\NewP\resources\localization | Select-Object -ExpandProperty FullName`
Expected: 输出待迁移本地化文件列表

- [ ] **Step 4: 调整前端本地化加载入口**

```ts
import platformCatalogZhs from '../../../../../modules/Platform/metadata/localization/zhs/dictionary/Platform.json';
```

- [ ] **Step 5: 运行本地化与枚举测试**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx`
Expected: PASS

- [ ] **Step 6: 更新验证文档并删除 resources 目录**

```md
## 2. 实际结果

- 平台不再依赖模块 manifest
- 平台改为扫描 `modules/*/metadata/**`
- `Platform` 枚举与本地化元数据已迁入 `modules/Platform/metadata/**`
- `resources/**` 已删除
```

- [ ] **Step 7: 提交本地化迁移与 resources 删除**

```bash
git add modules/Platform/metadata/localization frontend/packages/shell/src/localization/catalogs.ts frontend/packages/shell/src/__tests__/localization-sample.test.tsx docs/verification/metadata-authoring-layout-unification-verification.md
git add -u resources
git commit -m "重构：迁移平台共享本地化元数据并删除 resources"
```

## Self-Review

- 规格覆盖检查：本计划已覆盖取消模块清单、统一扫描根目录、保留 `schemaVersion`、使用 `type` 分发、建立平台注册表、迁移模型/枚举/本地化、删除 `resources/**`。
- 占位检查：所有 `expect(true).toBe(true)` 仅可作为计划占位，实施时必须替换为真实断言。
- 一致性检查：唯一 authoring 根目录始终是 `modules/<ModuleId>/metadata/**`；唯一内核执行级例外仍是 `server/platform-modeling/metadata/storage-types.json`。
