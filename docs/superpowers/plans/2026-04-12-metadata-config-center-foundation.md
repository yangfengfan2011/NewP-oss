# Metadata Config Center Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立独立的元数据配置中心模块，先打通 `SettingDefinition` 的定义层编辑闭环，并与 `ConfigCenter` 的值层职责完成边界收口。

**Architecture:** 后端新增独立 `MetadataConfigCenter` 模块，负责模块元数据发现、manifest 装配、`SettingDefinition` 读取与保存；`ConfigCenter` 保持 `ConfigItem` 值层承载与默认值同步能力。前端新增独立 `metadata-config-center` 模块，先提供模块列表、`SettingDefinition` 列表与保存入口，应用和 Git 同步首版只显示禁用按钮与状态说明，不执行真实动作。

**Tech Stack:** .NET 10, ASP.NET Core, xUnit, React, TypeScript

---

### Task 1: 固化目录契约与模块骨架

**Files:**
- Create: `server/modules/MetadataConfigCenter/MetadataConfigCenter.csproj`
- Create: `server/modules/MetadataConfigCenter/MetadataConfigCenterModule.cs`
- Create: `server/modules/MetadataConfigCenter/README.md`
- Create: `frontend/modules/metadata-config-center/package.json`
- Create: `frontend/modules/metadata-config-center/tsconfig.json`
- Create: `frontend/modules/metadata-config-center/src/index.ts`
- Create: `frontend/modules/metadata-config-center/src/pages/MetadataModuleOverview.tsx`
- Create: `tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj`
- Create: `tests/MetadataConfigCenter.Tests/MetadataConfigCenterModuleTests.cs`
- Create: `modules/ConfigCenter/metadata/metadata.manifest.json`
- Create: `modules/ConfigCenter/metadata/settings/ConfigCenter.settings.json`

- [ ] **Step 1: 写模块注册 failing test**

在 `tests/MetadataConfigCenter.Tests/MetadataConfigCenterModuleTests.cs` 添加最小测试，断言新模块暴露 `ModuleId = "MetadataConfigCenter"`，并注册 `/metadata-config/modules` 路由对应菜单。

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --filter MetadataConfigCenterModuleTests -nr:false -v minimal`
Expected: FAIL，提示 `MetadataConfigCenterModule` 或对应测试项目尚不存在。

- [ ] **Step 3: 创建后端模块最小骨架**

实现 `MetadataConfigCenterModule`，参照 [ConfigCenterModule.cs](D:/Github/ICP-X/NewP/server/modules/ConfigCenter/ConfigCenterModule.cs) 与 `ModuleTemplateSample` 结构，先注册菜单、权限，并为 `IMetadataManifestService` 预留注册位；具体 manifest interface/service 文件在 Task 2 创建，不引入其他业务逻辑。

- [ ] **Step 4: 创建前端模块最小骨架**

实现 `frontend/modules/metadata-config-center/src/index.ts` 与 `MetadataModuleOverview.tsx`，先展示“元数据配置中心”标题、一个静态模块表格和空状态提示。

- [ ] **Step 5: 创建首个 manifest 与 settings 样本**

在 `modules/ConfigCenter/metadata/metadata.manifest.json` 中启用 `settings` 类型，在 `modules/ConfigCenter/metadata/settings/ConfigCenter.settings.json` 中放入一条与 `ConfigCenterModule.GetConfigSchema()` 对齐的样本定义。

- [ ] **Step 6: 重新运行模块测试**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --filter MetadataConfigCenterModuleTests -nr:false -v minimal`
Expected: PASS

- [ ] **Step 7: 提交骨架**

```bash
git add server/modules/MetadataConfigCenter frontend/modules/metadata-config-center tests/MetadataConfigCenter.Tests modules/ConfigCenter/metadata
git commit -m "功能：新增元数据配置中心模块骨架"
```

### Task 2: manifest 装配与模块元数据发现

**Files:**
- Create: `server/modules/MetadataConfigCenter/Models/MetadataModuleManifest.cs`
- Create: `server/modules/MetadataConfigCenter/Models/MetadataTypeDescriptor.cs`
- Create: `server/modules/MetadataConfigCenter/Services/IMetadataManifestService.cs`
- Create: `server/modules/MetadataConfigCenter/Services/MetadataManifestService.cs`
- Create: `server/modules/MetadataConfigCenter/Controllers/MetadataModulesController.cs`
- Create: `tests/MetadataConfigCenter.Tests/MetadataManifestServiceTests.cs`
- Modify: `server/modules/MetadataConfigCenter/MetadataConfigCenterModule.cs`

- [ ] **Step 1: 写 manifest 解析 failing tests**

在 `tests/MetadataConfigCenter.Tests/MetadataManifestServiceTests.cs` 添加测试，断言服务能从 `modules/ConfigCenter/metadata/metadata.manifest.json` 读出：
- `moduleId = "ConfigCenter"`
- 启用 `settings`
- `sourcePath = "settings"`

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --filter MetadataManifestServiceTests -nr:false -v minimal`
Expected: FAIL，提示 manifest service 未实现。

- [ ] **Step 3: 实现 manifest 模型与服务**

实现 `MetadataManifestService`，只负责：
- 扫描 `modules/*/metadata/metadata.manifest.json`
- 反序列化 manifest
- 返回模块列表与启用类型摘要

- [ ] **Step 4: 暴露模块发现接口**

在 `MetadataModulesController` 提供 `GET /metadata-config/modules`，返回模块列表、可编辑类型和基础路径信息。

- [ ] **Step 5: 运行测试验证通过**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --filter "MetadataManifestServiceTests|MetadataConfigCenterModuleTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 6: 提交 manifest 装配能力**

```bash
git add server/modules/MetadataConfigCenter tests/MetadataConfigCenter.Tests
git commit -m "功能：新增模块元数据清单发现能力"
```

### Task 3: SettingDefinition 读取模型与保存闭环

**Files:**
- Create: `server/modules/MetadataConfigCenter/Models/SettingDefinition.cs`
- Create: `server/modules/MetadataConfigCenter/DTOs/SettingDefinitionDto.cs`
- Create: `server/modules/MetadataConfigCenter/DTOs/SaveSettingDefinitionsRequest.cs`
- Create: `server/modules/MetadataConfigCenter/Services/ISettingDefinitionAuthoringService.cs`
- Create: `server/modules/MetadataConfigCenter/Services/SettingDefinitionAuthoringService.cs`
- Create: `server/modules/MetadataConfigCenter/Adapters/SettingDefinitionAuthoringAdapter.cs`
- Create: `server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs`
- Create: `server/modules/MetadataConfigCenter/Controllers/SettingDefinitionsController.cs`
- Create: `tests/MetadataConfigCenter.Tests/SettingDefinitionAuthoringServiceTests.cs`
- Modify: `server/modules/MetadataConfigCenter/MetadataConfigCenterModule.cs`

- [ ] **Step 1: 写 SettingDefinition 读取/保存 failing tests**

在 `tests/MetadataConfigCenter.Tests/SettingDefinitionAuthoringServiceTests.cs` 添加两个测试：
- 读取 `ConfigCenter.settings.json` 后返回定义列表
- 保存修改后的 `DisplayName` 后，文件内容更新且返回 `已保存未应用` 状态

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --filter SettingDefinitionAuthoringServiceTests -nr:false -v minimal`
Expected: FAIL，提示 service 或 adapter 未实现。

- [ ] **Step 3: 实现 SettingDefinition adapter**

`SettingDefinitionAuthoringAdapter` 负责：
- 从 `modules/<ModuleId>/metadata/settings/*.json` 读取定义
- 转成页面编辑模型
- 校验 `Key` 唯一、`DefaultValue` 非空策略、`Group` 存在
- 生成待回写文件内容

- [ ] **Step 4: 实现受控文件写入**

`ModuleMetadataStore` 只负责：
- 路径解析
- 读取文本
- 版本摘要计算
- 原子写入

- [ ] **Step 5: 暴露 SettingDefinition API**

提供：
- `GET /metadata-config/modules/{moduleId}/settings`
- `PUT /metadata-config/modules/{moduleId}/settings`

- [ ] **Step 6: 运行测试验证通过**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --filter "SettingDefinitionAuthoringServiceTests|MetadataManifestServiceTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 7: 提交 SettingDefinition 闭环**

```bash
git add server/modules/MetadataConfigCenter tests/MetadataConfigCenter.Tests modules/ConfigCenter/metadata/settings
git commit -m "功能：打通 SettingDefinition 编辑闭环"
```

### Task 4: ConfigCenter 值层边界收口

**Files:**
- Modify: `server/modules/ConfigCenter/ConfigCenterModule.cs`
- Modify: `server/modules/ConfigCenter/Services/IConfigService.cs`
- Modify: `server/modules/ConfigCenter/Services/ConfigService.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigServiceTests.cs`
- Modify: `tests/ConfigCenter.Tests/LegacySettingImportServiceTests.cs`
- Modify: `docs/designs/configcenter-design.md`

- [ ] **Step 1: 写 failing tests 固化边界**

在 `ConfigServiceTests.cs` 增加测试，断言 `ConfigCenter` 只处理 `ConfigItem` 值层，不返回或编辑 `SettingDefinition` 文件结构；在 `LegacySettingImportServiceTests.cs` 补充注释和测试名称，强调导入目标是值层。

- [ ] **Step 2: 运行 ConfigCenter 定向测试确认失败**

Run: `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj --filter "ConfigServiceTests|LegacySettingImportServiceTests" -nr:false -v minimal`
Expected: FAIL，边界断言不满足或测试尚未存在。

- [ ] **Step 3: 调整 ConfigCenter 代码与命名**

在服务注释、接口注释和必要的实现位置明确：
- `ConfigDescriptor` 对应 `SettingDefinition` 默认值同步入口
- `ConfigItem` 是值层模型
- `ConfigCenter` 不承担元数据定义编辑职责

- [ ] **Step 4: 重新运行 ConfigCenter 定向测试**

Run: `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj --filter "ConfigServiceTests|LegacySettingImportServiceTests|ConfigCenterModuleTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 5: 提交 ConfigCenter 边界收口**

```bash
git add server/modules/ConfigCenter tests/ConfigCenter.Tests docs/designs/configcenter-design.md
git commit -m "重构：收紧 ConfigCenter 值层职责边界"
```

### Task 5: 前端元数据配置中心最小工作台

**Files:**
- Modify: `frontend/modules/metadata-config-center/src/index.ts`
- Create: `frontend/modules/metadata-config-center/src/services/metadataConfigCenterService.ts`
- Create: `frontend/modules/metadata-config-center/src/pages/SettingDefinitionManagement.tsx`
- Create: `frontend/modules/metadata-config-center/src/components/ModuleTypeSidebar.tsx`
- Create: `frontend/modules/metadata-config-center/src/components/SaveApplyToolbar.tsx`
- Create: `frontend/modules/metadata-config-center/src/components/SettingDefinitionTable.tsx`
- Modify: `frontend/packages/shell/src/registerModules.ts`
- Modify: `frontend/packages/shell/src/builtin-runtime-plugins.ts`

- [ ] **Step 1: 写页面最小交互 failing test 或 smoke 清单**

如果现有前端模块没有测试基础，至少在计划执行时补一个手工 smoke 场景：
- 打开 `/metadata-config/modules`
- 看到 `ConfigCenter` 模块
- 打开 `SettingDefinition` 列表
- 编辑 `DisplayName`
- 点击保存后看到 `已保存未应用`

- [ ] **Step 2: 实现服务层请求**

`metadataConfigCenterService.ts` 提供：
- `getModules()`
- `getSettings(moduleId)`
- `saveSettings(moduleId, payload)`

- [ ] **Step 3: 实现页面骨架**

页面包含：
- 左侧模块/类型树
- 中间 `SettingDefinition` 表格
- 顶部 `保存 / 应用 / Git 同步` 工具栏

首版 `应用` 和 `Git 同步` 可先展示禁用态或说明文案，但 `保存` 必须可用。

- [ ] **Step 4: 接入 Shell**

将 `metadata-config-center` 作为独立模块接入，与 `config-center` 并列，而不是挂到 `config-center` 子页下。

- [ ] **Step 5: 运行前端构建**

Run: `pnpm --filter @icp-platform/module-metadata-config-center build`
Expected: PASS

- [ ] **Step 6: 提交前端最小工作台**

```bash
git add frontend/modules/metadata-config-center frontend/packages/shell
git commit -m "功能：新增元数据配置中心前端工作台"
```

### Task 6: 保存状态、应用入口与未应用提示

**Files:**
- Modify: `server/modules/MetadataConfigCenter/DTOs/SettingDefinitionDto.cs`
- Modify: `server/modules/MetadataConfigCenter/DTOs/SaveSettingDefinitionsRequest.cs`
- Modify: `server/modules/MetadataConfigCenter/Services/SettingDefinitionAuthoringService.cs`
- Modify: `frontend/modules/metadata-config-center/src/pages/SettingDefinitionManagement.tsx`
- Modify: `frontend/modules/metadata-config-center/src/components/SaveApplyToolbar.tsx`
- Create: `tests/MetadataConfigCenter.Tests/SettingDefinitionStatusTests.cs`

- [ ] **Step 1: 写状态流转 failing tests**

在 `SettingDefinitionStatusTests.cs` 断言保存成功后返回：
- `hasUnsavedChanges = false`
- `hasUnappliedChanges = true`
- `gitSyncPending = true`

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --filter SettingDefinitionStatusTests -nr:false -v minimal`
Expected: FAIL

- [ ] **Step 3: 实现状态字段与页面提示**

后端返回保存后的状态摘要；前端工具栏按状态显示：
- `保存成功，尚未应用`
- `Git 未同步`

- [ ] **Step 4: 运行测试与前端构建**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --filter "SettingDefinitionStatusTests|SettingDefinitionAuthoringServiceTests" -nr:false -v minimal`
Expected: PASS

Run: `pnpm --filter @icp-platform/module-metadata-config-center build`
Expected: PASS

- [ ] **Step 5: 提交状态流转**

```bash
git add server/modules/MetadataConfigCenter frontend/modules/metadata-config-center tests/MetadataConfigCenter.Tests
git commit -m "功能：补齐元数据保存状态提示"
```

### Task 7: 文档、验证与交付收口

**Files:**
- Modify: `docs/designs/metadata-config-center-design.md`
- Modify: `docs/designs/configcenter-design.md`
- Modify: `docs/current-status.md`
- Modify: `docs/verification/verification-matrix.md`
- Create: `docs/verification/metadata-config-center-foundation-verification.md`

- [ ] **Step 1: 运行后端测试**

Run: `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj -nr:false -v minimal`
Expected: PASS

Run: `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj --filter "ConfigServiceTests|LegacySettingImportServiceTests|ConfigCenterModuleTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 2: 运行前端构建**

Run: `pnpm --filter @icp-platform/module-metadata-config-center build`
Expected: PASS

Run: `pnpm --filter @icp-platform/module-config-center build`
Expected: PASS

- [ ] **Step 3: 执行最小手工 smoke**

记录以下结果到 `docs/verification/metadata-config-center-foundation-verification.md`：
- 元数据配置中心页面可访问
- 可读取 `ConfigCenter` 模块的 `SettingDefinition`
- 保存后状态为“已保存未应用”
- `ConfigCenter` 仍只承载值层配置项

- [ ] **Step 4: 更新状态文档**

在 `docs/current-status.md` 和 `docs/verification/verification-matrix.md` 补充：
- 元数据配置中心独立模块已建立
- `SettingDefinition / ConfigItem / ConfigCenter` 边界已收口

- [ ] **Step 5: 提交文档与验证**

```bash
git add docs/designs docs/current-status.md docs/verification
git commit -m "文档：记录元数据配置中心基础能力验证"
```
