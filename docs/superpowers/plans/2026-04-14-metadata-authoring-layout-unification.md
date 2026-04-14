# 元数据 Authoring 布局统一实施计划

> 面向 agent 执行：实施时应使用 `subagent-driven-development` 或 `executing-plans` 技能逐项推进，并用 `- [ ]` 复选框跟踪状态。

**目标：** 取消模块清单，统一将权威元数据收口到 `modules/<ModuleId>/metadata/**`，建立 `modules/Platform/metadata/**` 与 `server/platform/metadata/**`，迁移现有模型、枚举和本地化元数据，并移除 `resources/**` 元数据入口。

**架构要点：** 平台不再依赖模块 manifest。改为扫描 `modules/*/metadata/**` 下的元数据文件，通过文件头中的 `schemaVersion` 和 `type` 判定结构版本与元数据类型，再由 `server/platform/metadata/file-type-mappings.json` 统一声明 adapter 与 `mergeMode`。平台默认使用 `replace`，仅 `dic` 使用 `dic-entry`。迁移按“模块模型元数据先收口，再迁平台共享元数据，最后删除旧入口”的顺序推进。所有冲突、未知类型、版本不兼容都必须显式失败，不允许依赖扫描顺序或旧入口兜底。

**技术栈：** .NET 10、PowerShell、JSON 元数据文件、现有 loader/validator/projector 链路、前端静态资源导入、`docs/designs` 与 `docs/superpowers/plans` 下的仓库文档

---

### Task 1: 收口设计与验证基线文档

**Files:**
- Modify: `docs/designs/metadata-authoring-layout-unification-design.md`
- Create: `docs/verification/metadata-authoring-layout-unification-verification.md`

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
- Create: `modules/ConfigCenter/metadata/models/ConfigItem.model.json`
- Modify: `modules/Platform/metadata/dictionaries/*.dic.json`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- Delete: `server/modules/ConfigCenter/metadata/models/ConfigItem.model.json`
- Test: `tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`

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

- [ ] **Step 3.1: 同步更新模型对应的 `dic` 文件**

要求：

- 按既有模型多语言 key 约定，更新对应 `modules/Platform/metadata/dictionaries/*.dic.json`
- 不在 `model` 文件内嵌多语言内容
- 若模型新增或变更了需要展示的对象/字段，必须同步补齐对应 `dic` 词条
- 更新后的词条必须覆盖 `languages.json` 中声明的全部语言

- [ ] **Step 4: 运行定向测试确认通过**

Run: `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 5: 运行建模测试确认投影链未损坏**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal`
Expected: PASS

- [ ] **Step 5.1: 验证模型对应 `dic` 文件已同步更新**

至少确认：

- 对应 `dic` 文件存在
- 新增或变更的模型多语言 key 可解析
- 每个相关词条覆盖 `languages.json` 中声明的全部语言

- [ ] **Step 6: 提交模型元数据迁移**

```bash
git add modules/ConfigCenter/metadata/models/ConfigItem.model.json modules/Platform/metadata/dictionaries server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs
git commit -m "重构：迁移 ConfigCenter 模型元数据目录"
```

### Task 3: 建立平台文件类型映射与扫描入口

**Files:**
- Create: `server/platform/metadata/file-type-mappings.json`
- Modify: `server/modules/MetadataConfigCenter/Services/MetadataManifestService.cs`
- Modify: `server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs`
- Test: `tests/MetadataConfigCenter.Tests/MetadataManifestServiceTests.cs`

- [ ] **Step 1: 为“取消 manifest、改为目录扫描”补失败测试**

```csharp
[Fact]
public async Task GetModuleMetadataRootsAsync_Scans_Module_Metadata_Directories()
{
    Assert.True(true);
}
```

说明：执行时必须将占位断言替换为真实目录扫描断言。

- [ ] **Step 1.1: 补充失败与冲突断言**

至少覆盖以下情况：

- 未注册 `type` 会失败
- 不支持的 `schemaVersion` 会失败
- 未声明特例类型却尝试触发合并语义会失败
- `dic-entry` 对非 `dic` 类型会失败
- 失败时错误中包含模块与文件路径

- [ ] **Step 2: 运行定向测试确认现状仍依赖 manifest**

Run: `dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj --filter "FullyQualifiedName~MetadataManifestServiceTests" -nr:false -v minimal`
Expected: FAIL 或现有测试只覆盖 manifest 行为

- [ ] **Step 3: 创建平台文件类型映射**

```json
{
  "schemaVersion": "1.0",
  "mappings": {
    "model": {
      "adapter": "ModelMetadataAuthoringAdapter",
      "mergeMode": "replace"
    },
    "settings": {
      "adapter": "SettingDefinitionAuthoringAdapter",
      "mergeMode": "replace"
    },
    "enum": {
      "adapter": "EnumerationAuthoringAdapter",
      "mergeMode": "replace"
    },
    "dic": {
      "adapter": "DictionaryAuthoringAdapter",
      "mergeMode": "dic-entry"
    }
  }
}
```

- [ ] **Step 4: 将模块发现逻辑从 manifest 改为扫描 `modules/*/metadata/**`**

Run: `dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj --filter "FullyQualifiedName~MetadataManifestServiceTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 5: 提交平台文件类型映射与扫描入口调整**

```bash
git add server/platform/metadata/file-type-mappings.json server/modules/MetadataConfigCenter/Services/MetadataManifestService.cs server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs tests/MetadataConfigCenter.Tests/MetadataManifestServiceTests.cs
git commit -m "重构：改为扫描模块元数据目录并引入平台文件类型映射"
```

### Task 4: 建立 Platform 模块 metadata 根目录并迁移枚举

**Files:**
- Create: `modules/Platform/metadata/enumerations/`
- Delete: `resources/enumerations/*.json`
- Modify: `frontend/packages/shell/src/localization/catalogs.ts`
- Test: `frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx`

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

Run: `Get-ChildItem .\resources\enumerations -Filter *.json | Select-Object -ExpandProperty Name`
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

### Task 5: 迁移平台 `dic` 元数据并删除旧本地化目录

**Files:**
- Create: `modules/Platform/metadata/dictionaries/`
- Create: `modules/Platform/metadata/languages.json`
- Delete: `resources/localization/**`
- Modify: `frontend/packages/shell/src/localization/catalogs.ts`
- Test: `frontend/packages/shell/src/__tests__/localization-sample.test.tsx`
- Modify: `docs/verification/metadata-authoring-layout-unification-verification.md`

- [ ] **Step 1: 为 `dic` 新结构补失败测试**

```ts
it('loads dictionary catalogs from dic metadata files', async () => {
  expect(true).toBe(true);
});
```

- [ ] **Step 2: 运行定向测试确认旧路径依赖存在**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx`
Expected: FAIL

- [ ] **Step 3: 迁移语言配置、字典和枚举本地化资源到 Platform 模块**

Run: `Get-ChildItem -Recurse .\resources\localization | Select-Object -ExpandProperty FullName`
Expected: 输出待迁移本地化文件列表

- [ ] **Step 4: 将旧按语言目录资源重组为 `dic` 文件**

要求：

- 字典类资源重组到 `modules/Platform/metadata/dictionaries/*.dic.json`
- 枚举本地化资源也重组到 `modules/Platform/metadata/dictionaries/*.dic.json`
- 每个 `dic` 文件内部直接承载完整多语言
- 每个词条必须覆盖 `languages.json` 中声明的全部语言

- [ ] **Step 5: 调整前端本地化加载入口**

```ts
import platformCatalog from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';
```

- [ ] **Step 6: 运行本地化与枚举测试**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx`
Expected: PASS

- [ ] **Step 7: 更新验证文档并删除 resources 目录**

```md
## 2. 实际结果

- 平台不再依赖模块 manifest
- 平台改为扫描 `modules/*/metadata/**`
- `Platform` 枚举元数据已迁入 `modules/Platform/metadata/enumerations/**`
- `Platform` 字典与枚举本地化已统一重组为 `modules/Platform/metadata/dictionaries/*.dic.json`
- `resources/**` 已删除
```

- [ ] **Step 8: 提交本地化迁移与 resources 删除**

```bash
git add modules/Platform/metadata/dictionaries modules/Platform/metadata/languages.json frontend/packages/shell/src/localization/catalogs.ts frontend/packages/shell/src/__tests__/localization-sample.test.tsx docs/verification/metadata-authoring-layout-unification-verification.md
git add -u resources
git commit -m "重构：迁移平台共享 dic 元数据并删除旧本地化目录"
```

### Task 6: 完整验证与回滚演练记录

**Files:**
- Modify: `docs/verification/metadata-authoring-layout-unification-verification.md`

- [ ] **Step 1: 运行后端构建**

Run: `dotnet build .\ICP-Platform.sln -v minimal`
Expected: PASS

- [ ] **Step 2: 运行后端测试**

Run: `dotnet test .\ICP-Platform.sln -nr:false -v minimal`
Expected: PASS

- [ ] **Step 3: 运行前端构建**

Run: `pnpm --dir .\frontend build`
Expected: PASS

- [ ] **Step 4: 运行前端测试**

Run: `pnpm --dir .\frontend test`
Expected: PASS

- [ ] **Step 5: 运行契约检查或 smoke 验证**

至少执行一项与本次改动直接相关的真实验证，例如：

- MetadataConfigCenter 模块接口 smoke
- 前端实际加载枚举与本地化目录的 smoke
- 平台文件类型映射驱动的目录扫描 smoke
- `dic-entry` 行为 smoke，确认词条按条目合并且同词条整组多语言覆盖

Expected: PASS，并能证明旧入口未被静默回退使用

- [ ] **Step 6: 记录回滚方案验证结果**

验证文档至少补充：

- 回滚触发条件
- 回滚最小步骤
- 本次实施实际执行了哪些回滚预检查
- 如果失败，如何恢复 `resources/**` 与旧入口引用

- [ ] **Step 7: 提交完整验证记录**

```bash
git add docs/verification/metadata-authoring-layout-unification-verification.md
git commit -m "验证：补充元数据目录统一验证与回滚记录"
```

## Self-Review

 - 规格覆盖检查：本计划已覆盖取消模块清单、统一扫描根目录、保留 `schemaVersion`、使用 `type` 分发、建立平台文件类型映射、迁移模型/枚举/本地化、删除 `resources/**`。
- 占位检查：所有 `expect(true).toBe(true)` 仅可作为计划占位，实施时必须替换为真实断言。
 - 一致性检查：唯一 authoring 根目录始终是 `modules/<ModuleId>/metadata/**`；唯一内核执行级保留目录收口为 `server/platform/metadata/**`。
- 路径检查：本计划内的仓库文件路径统一使用仓库相对路径，不再写仓库外绝对路径。
- 验证检查：实施完成前，必须补齐后端构建、后端测试、前端构建、前端测试，以及至少一项契约检查或 smoke 验证。
- 回滚检查：实施记录必须包含触发条件、回滚步骤和实际回滚预检查结论。
