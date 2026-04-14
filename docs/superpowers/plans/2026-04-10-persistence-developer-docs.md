# Persistence Developer Docs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为仓库内部开发者补齐持久化公共 API、使用方式和开发流程三份正式手册，并补上最小入口链接。

**Architecture:** 文档按“API 说明”“使用说明”“研发流程”三条线拆分，统一落在 `docs/guides/`。内容以当前已落地代码和真实命令为准，通过少量交叉引用把读者从状态文档和测试 README 导向新手册。

**Tech Stack:** Markdown、PowerShell、.NET 10、EF Core 10、多 Provider 持久化基础层

---

### Task 1: 梳理持久化公共入口与现有使用链路

**Files:**
- Read: `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`
- Read: `server/platform-persistence/PersistenceOptions.cs`
- Read: `server/platform-persistence/Abstractions/IPersistenceMigrationRunner.cs`
- Read: `server/platform-persistence/Abstractions/ISqlDialect.cs`
- Read: `server/platform-persistence/Abstractions/ISqlQueryExecutor.cs`
- Read: `server/platform-persistence-efcore/Extensions/EfCorePersistenceServiceCollectionExtensions.cs`
- Read: `server/platform-persistence-efcore/EfPlatformDbContext.cs`
- Read: `server/platform-persistence-composition/PlatformPersistenceModelCatalog.cs`
- Read: `server/platform-modeling/Definitions/IModelDefinitionProvider.cs`
- Read: `tests/integration/persistence/Invoke-PersistenceSmoke.ps1`

- [ ] **Step 1: 读取当前真实代码入口**

Run:

```powershell
Get-Content .\apps\host-api\Extensions\PersistenceBootstrapExtensions.cs
Get-Content .\server\platform-persistence\PersistenceOptions.cs
Get-Content .\server\platform-persistence-efcore\Extensions\EfCorePersistenceServiceCollectionExtensions.cs
Get-Content .\server\platform-persistence-composition\PlatformPersistenceModelCatalog.cs
Get-Content .\tests\integration\persistence\Invoke-PersistenceSmoke.ps1
```

Expected:

- 能确认宿主入口、配置模型、EF 注册入口、模型定义聚合入口与 smoke 主脚本的真实实现。

- [ ] **Step 2: 归纳要写入手册的稳定入口**

需要整理出以下清单并作为后续文档的基础：

- 宿主入口：
  - `AddPlatformPersistence`
  - `ApplyPlatformPersistenceAsync`
- EF Core 入口：
  - `AddEfCorePersistence`
- 配置模型：
  - `PersistenceOptions`
  - `PersistenceProvider`
  - `PersistenceAdapterKind`
- 核心抽象：
  - `IModelDefinitionProvider`
  - `ISqlDialect`
  - `ISqlQueryExecutor`
  - `IPersistenceMigrationRunner`
  - `EfPlatformDbContext`
  - `PlatformPersistenceModelCatalog`
- 使用链路：
  - `appsettings` / 环境变量
  - 三套 migration 项目
  - 三套 smoke 脚本

- [ ] **Step 3: 检查现有文档的衔接点**

Run:

```powershell
Get-Content .\docs\current-status.md -Encoding UTF8 | Select-Object -Skip 286 -First 24
Get-Content .\tests\README.md -Encoding UTF8
```

Expected:

- 能定位 `Phase 6.5` 状态段落和 smoke 段落，确认后续补入口链接的位置。

### Task 2: 编写三篇持久化开发者手册

**Files:**
- Create: `docs/guides/persistence-public-api.md`
- Create: `docs/guides/persistence-usage-guide.md`
- Create: `docs/guides/persistence-development-workflow.md`

- [ ] **Step 1: 编写公共 API 手册**

文档必须包含：

- 文档定位与适用范围
- 宿主入口说明
- EF Core 入口说明
- 配置模型说明
- 核心抽象说明
- “谁应该依赖 / 谁不应该直接依赖”边界提示
- 代码锚点路径

关键入口至少覆盖：

```text
AddPlatformPersistence
ApplyPlatformPersistenceAsync
AddEfCorePersistence
PersistenceOptions
IModelDefinitionProvider
ISqlDialect
ISqlQueryExecutor
IPersistenceMigrationRunner
EfPlatformDbContext
PlatformPersistenceModelCatalog
```

- [ ] **Step 2: 编写使用指南**

文档必须包含：

- 宿主如何启用平台持久化
- `Persistence` 配置段说明
- `ConnectionStrings` 与环境变量覆盖方式
- Provider 切换方式
- 三套 migration 项目如何使用
- `dotnet-ef database update` 示例
- 三套 smoke 脚本示例
- 当前首批范围与边界

至少给出以下命令样例：

```powershell
dotnet tool restore
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-OracleSmoke.ps1
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-MySqlSmoke.ps1
```

- [ ] **Step 3: 编写开发流程手册**

文档必须包含：

- 推荐开发顺序
- 每一步的主要修改目录
- 模块新增/修改时应补哪些验证
- 常见越层问题
- 文档与验证收口要求

核心流程至少覆盖：

```text
业务边界 -> 模型语义 -> EF 适配 -> 宿主接入 -> 迁移/Smoke -> 文档收口
```

- [ ] **Step 4: 自查三篇文档的一致性**

Run:

```powershell
Select-String -Path .\docs\guides\persistence-public-api.md,.\docs\guides\persistence-usage-guide.md,.\docs\guides\persistence-development-workflow.md -Pattern 'AddPlatformPersistence|ApplyPlatformPersistenceAsync|AddEfCorePersistence|SqlServer|Oracle|MySql|smoke'
```

Expected:

- 三篇文档都已经落地。
- 关键入口、Provider 名称和 smoke 关键字能被检索到。

### Task 3: 补最少量入口链接并验证可发现性

**Files:**
- Modify: `docs/current-status.md`
- Modify: `tests/README.md`

- [ ] **Step 1: 在状态文档中增加开发者手册入口**

在 `Phase 6.5` 已完成段落附近补一小段“开发者手册入口”，至少包含：

- `persistence-public-api.md`
- `persistence-usage-guide.md`
- `persistence-development-workflow.md`

- [ ] **Step 2: 在测试 README 中增加使用指南引用**

在多数据库持久化 smoke 段落补一条提示，告诉开发者：

- 如果想看宿主配置、Provider 切换、迁移项目用法，请转到 `docs/guides/persistence-usage-guide.md`

- [ ] **Step 3: 验证入口文档能检索到新链接**

Run:

```powershell
Select-String -Path .\docs\current-status.md,.\tests\README.md -Pattern 'persistence-public-api|persistence-usage-guide|persistence-development-workflow'
```

Expected:

- `docs/current-status.md` 能检索到三篇新文档名。
- `tests/README.md` 能检索到 `persistence-usage-guide.md`。

- [ ] **Step 4: 完整回读本轮新增文档**

Run:

```powershell
Get-Content .\docs\guides\persistence-public-api.md -Encoding UTF8
Get-Content .\docs\guides\persistence-usage-guide.md -Encoding UTF8
Get-Content .\docs\guides\persistence-development-workflow.md -Encoding UTF8
```

Expected:

- 文档无占位符、无明显事实错误、无“未来能力伪装成当前能力”的表述。

