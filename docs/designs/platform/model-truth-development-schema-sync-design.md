# `model.json` 真源统一与开发环境自动结构同步设计

## 1. 背景

当前仓库已经同时存在三条彼此相关、但还没有完全收口到同一真源的链路：

- 持久化运行时通过 `PlatformPersistenceModelCatalog -> ModelDefinition -> EfPlatformDbContext` 组装数据库模型。
- 开发环境数据库更新目前仍主要依赖 EF Core migration 三件套与自动 `Database.MigrateAsync()`。
- `modules/<ModuleId>/metadata/models/*.model.json` 已经被定义为模型 authoring 入口，但当前只有少数模型真正接入。

这导致开发阶段存在三个明显问题：

1. 即使只是本地开发联调，也往往要先生成 migration 三件套，系统才能正常启动。
2. `model.json` 被声明为 authoring 真源，但运行时仍然混有手写 `ModelDefinition`，真源口径不一致。
3. 一部分基础 DTO 仍然是手写的，字段结构与 `ModelDefinition`、`model.json` 没有统一同步链。

截至 2026-04-23，当前仓库的实际状态是：

- 已有 `model.json` 真源：
  - `modules/ConfigCenter/metadata/models/ConfigItem.model.json`
  - `modules/Organization/metadata/models/Person.model.json`
- 仍依赖手写 `ModelDefinition` 的模型：
  - `ValueListItem`
  - `Role`
  - `Permission`
  - `RolePermission`
  - `UserRole`
  - `Company`
  - `Department`
  - `Position`
  - `PersonUserLink`

如果不先把“模型真源、开发库同步、基础 DTO”统一到同一条可验证链路，后续无论是 AI 辅助开发、模块复制、还是开发态快速试错，都会持续被“改了 `model.json` 但系统并不真的以它为准”这一类问题卡住。

本设计的目标，是在不削弱正式 migration 发布治理的前提下，把开发环境的数据库结构同步与基础 DTO 同步都收口到 `model.json` 真源，并把手写 `ModelDefinition` 从长期架构中退出。

## 2. 目标

- 让 `modules/<ModuleId>/metadata/models/*.model.json` 成为受管持久化模型的唯一 authoring 真源。
- 让开发环境在 `SqlServer` 下可以不依赖新生成 migration 三件套也能正常启动。
- 让 `PlatformPersistenceModelCatalog` 从“手写定义真源”收口为“由 `model.json` 投影结果聚合出的运行时视图”。
- 把“模型同构基础 DTO”纳入统一同步链路，与 `model.json` 保持一致。
- 开发环境启动时只自动执行低风险结构变更；若存在高风险动作、真源不一致或同步失败，则阻止启动并给出明确信号。
- 正式环境、发布链路和 migration 审计能力保持不变。

## 3. 非目标

- 不在本设计中取消或删除现有 migration 工程。
- 不在本设计中让 MySQL、Oracle 同时支持开发态自动结构同步。
- 不在本设计中把请求 DTO、聚合 DTO、树 DTO、详情 DTO、流程 DTO 全部改成自动生成。
- 不在本设计中引入运行时动态 DTO 或弱类型 API 契约。
- 不在本设计中处理复杂数据搬迁、跨表重组、数据修复脚本自动化。
- 不在本设计中把插件模块的数据库模型也纳入首轮自动结构同步。

## 4. 范围

本轮设计涉及：

- `modules/<ModuleId>/metadata/models/*.model.json`
- `server/platform-modeling/**`
- `server/platform-persistence-composition/**`
- `server/platform-persistence-efcore/**`
- `apps/host-api/**`
- `server/tools/dev-schema-sync/**`
- `server/tools/model-dto-sync/**`
- `server/modules/*/Persistence/Descriptors/**`
- `server/modules/*/DTOs/**`
- 与上述链路直接相关的测试与验证文档

本轮设计不涉及：

- 前端 Shell / SDK 接入机制改造
- 模块私有业务服务重构
- CI/CD 流水线改造
- 插件打包协议调整
- 发布中心或数据库版本中心

## 5. 设计约束

- `model.json` 是唯一 authoring 真源；运行时结果视图、生成代码和 catalog 都不能反向成为真源。
- 元数据仍只表达声明性输入；装载、校验、投影、风险判断、执行、回滚信号都必须收口在代码内核。
- 开发环境自动结构同步只允许在 `Development + SqlServer` 下启用。
- 启用自动结构同步时，不允许静默回退到手写 `ModelDefinition` 或 migration 自动改库。
- 高风险结构变更不能在开发启动时被隐式执行。
- 基础 DTO 自动化范围必须受控，只覆盖“模型同构基础 DTO”。
- 模型真源、运行时投影和 DTO 同步之间如果发生不一致，必须失败并显式暴露，不允许“先跑起来再说”。
- 任何新的字段语义扩展都要明确归属：数据库结构语义属于 `storage`；DTO/契约投影语义不能反向污染数据库结构。

## 6. 总体方案

### 6.1 方案概述

采用“`model.json` 真源统一 + 模块 provider 投影 + 开发环境启动前自动 schema sync + 基础 DTO 同步”的方案：

1. 为所有受管持久化模型补齐 `model.json`。
2. 所有模块的 `ModelDefinition` 都由共享 metadata loader 从 `model.json` 投影生成，不再手写常量定义。
3. `PlatformPersistenceModelCatalog` 只聚合各模块 provider 的投影结果。
4. 开发环境启动时，在进入现有 migration runner 之前，先执行严格的一致性检查。
5. 一致性检查通过后，基于 `model.json` 投影结果生成目标 schema，与当前 SqlServer 实际结构做 diff。
6. 若计划为空，则直接启动；若全是低风险动作，则自动执行后启动；若包含高风险动作或执行失败，则阻止启动。
7. 模型同构基础 DTO 也由同一份 `model.json` 驱动生成，并通过签名清单参加一致性检查。

### 6.2 为什么选这个方案

- 它满足“开发环境不需要先生成 migration 三件套也能运行”的直接目标。
- 它保持 `model.json` 作为唯一真源，不再让 catalog、DTO 或 migration 快照分裂为并行真源。
- 它把开发态快速迭代与正式发布治理分开：开发期走自动 schema sync，正式环境继续走 migration。
- 它保留强类型 `ModelDefinition`、EF 投影和 DTO 代码产物，避免把运行时推向动态解释器。
- 它通过一致性检查和高风险阻断，把“自动化便利”控制在可验证、可回退的范围内。

### 6.3 不选的方案

- 不选“继续只依赖 migration 三件套”：
  - 不能解决开发期反馈回路过长的问题。
- 不选“启动时直接调用现有 CLI 文本工具”：
  - CLI 当前面向人工交互，不适合作为运行时内核。
- 不选“让 catalog 继续接受手写定义与 `model.json` 混用”：
  - 会永久保留双真源。
- 不选“把所有 DTO 一次性都自动生成”：
  - 会把本轮范围扩张为 API 契约全面重做，风险过高。

## 7. 关键模型

### 7.1 模型真源清单

- `ModelSourceInventory`
  - 表示当前平台受管持久化模型的真源清单。
  - 每个模型必须对应一份 `modules/<ModuleId>/metadata/models/<ModelName>.model.json`。
  - 若运行时需要的模型在真源清单中缺失，则开发环境自动同步不可启用。

### 7.2 模块级模型 provider

- `IModelDefinitionProvider`
  - 继续作为模块对外暴露 `ModelDefinition` 集合的契约。
- `ModuleModelMetadataProviderBase`
  - 共享扫描、加载、校验、投影逻辑。
- `<ModuleId>ModelMetadataProvider`
  - 每个模块一个小型 provider，只声明模块 ID 与模型目录，不再手写字段与索引常量。

模块 provider 输出的是运行时结果视图，而不是新的 authoring 真源。

provider 读取元数据文件时，首轮采用双根目录查找顺序：

1. 运行输出目录下的 `modules/<ModuleId>/metadata/models/`
2. 仓库根目录下的 `modules/<ModuleId>/metadata/models/`

规则：

- 运行时优先读取输出目录，避免部署态强依赖仓库根目录结构。
- 设计时 migration 生成与本地测试允许回退到仓库根目录。
- 两个根目录都找不到时，直接失败，不做静默跳过。

### 7.3 Catalog 结果视图

- `PlatformPersistenceModelCatalog`
  - 只负责聚合模块 provider 的投影结果。
  - 不再直接拼接手写 `AuthorizationModelDefinitions.All` 之类常量。
  - 聚合阶段需要检查：
    - 模型名重复
    - 表名重复
    - provider 输出为空
    - 模块声明与真源文件不一致

### 7.4 开发环境结构同步选项

新增配置节：

```json
{
  "DevelopmentSchemaSync": {
    "Enabled": false
  }
}
```

行为固定，不提供“启动时放行高风险”这类开关。

### 7.5 字段契约语义扩展

为了让同一份 `model.json` 同时支撑数据库结构与基础 DTO 投影，本轮在字段级新增受控 `contract` 分区：

```json
{
  "name": "EmploymentStatus",
  "storage": {
    "type": "string",
    "nullable": true
  },
  "contract": {
    "valueType": "enum",
    "clrType": "EmploymentStatus"
  },
  "dataSource": {
    "type": "enum",
    "key": "EmploymentStatus"
  }
}
```

规则如下：

- `storage` 继续只负责数据库结构，不负责 CLR/DTO 类型。
- `contract` 只负责基础 DTO 与契约投影，不参与数据库结构计算。
- `contract.valueType` 首轮允许值对齐 `FieldValueType` 的受控子集：
  - `text`
  - `description`
  - `bool`
  - `datetime`
  - `objectId`
  - `enum`
- `contract.clrType` 仅在需要强类型 DTO 投影时使用。
- 对于 `enum` 字段：
  - 若显式提供 `contract.clrType`，DTO 生成器使用该 CLR 类型名。
  - 若未显式提供但 `dataSource.type = enum`，则默认按 `ICP.Platform.Modules.<ModuleId>.Models.<dataSource.key>` 约定查找 CLR 枚举类型。
  - 若仍无法解析，则 DTO 同步失败，并阻止开发环境启动。

### 7.6 基础 DTO 同步模型

- `BaseDtoDescriptor`
  - 表示从单个模型投影出来的基础 DTO 描述。
- `BaseDtoSyncManifest`
  - 记录每个基础 DTO 的签名摘要。
- `IBaseDtoSignatureProvider`
  - 模块程序集对外提供已编译基础 DTO 签名的接口，用于开发启动一致性检查。

基础 DTO 的生成范围：

- 覆盖：
  - `<ModelName>Dto`
  - 与模型字段一一对应的展示值伴生字段
- 不覆盖：
  - `*Request`
  - `*DetailDto`
  - `*SummaryDto`
  - `*TreeNodeDto`
  - 任何跨模型聚合 DTO

推荐生成位置：

- `server/modules/<ModuleId>/DTOs/Generated/<ModelName>Dto.g.cs`
- `server/modules/<ModuleId>/DTOs/Generated/<ModuleId>BaseDtoSignatureProvider.g.cs`

### 7.7 结构同步计划模型

沿用并增强现有 `SchemaPlan`、`SchemaOperation`、`RiskLevel`：

- `Safe`
  - 可在开发启动时自动执行
- `Confirm`
  - 一律阻止启动，并要求开发者显式处理

`Confirm` 级别至少包括：

- `DropColumn`
- `RenameTable`
- `RenameColumn`
- `AlterColumnType`
- `AlterNullability`（可空收紧）

## 8. 核心流程

### 8.1 主流程：开发环境启动前自动结构同步

1. Host 启动时读取 `DevelopmentSchemaSync:Enabled`。
2. 若未启用，继续走现有 migration 启动链路。
3. 若已启用，先验证当前环境是否满足：
   - `IHostEnvironment.IsDevelopment() == true`
   - `Persistence:Provider == SqlServer`
4. 读取所有模块的 `model.json` 真源并投影为 `ModelDefinition`。
5. 执行真源一致性检查：
   - 受管模型是否都有 `model.json`
   - catalog 聚合结果是否完全来自 provider
   - 是否仍存在手写 `ModelDefinition` 作为运行时输入
   - 基础 DTO 签名是否与当前模型投影一致
6. 若一致性检查失败，直接阻止启动。
7. 一致性检查通过后，读取当前 SqlServer 实际 schema。
8. 生成 `SchemaPlan` 并补齐 SQL preview。
9. 若 `SchemaPlan` 无操作，直接继续启动。
10. 若所有操作都是 `Safe`，在单事务中自动执行。
11. 执行成功后继续启动。
12. 若出现任一 `Confirm` 操作或 SQL 执行失败，阻止启动。

### 8.2 异常流程：高风险结构变更

1. 生成 `SchemaPlan`。
2. 发现 `Confirm` 级操作。
3. 输出：
   - 风险摘要
   - 涉及表/列
   - 建议开发者显式处理的方式
4. Host 终止启动，不执行任何数据库变更。

### 8.3 异常流程：模型真源不一致

以下任一情况直接视为不一致：

- 某受管模型没有对应 `model.json`
- provider 未加载到应有模型
- catalog 仍然引用手写 `ModelDefinition`
- DTO 签名与当前模型投影不一致
- `contract` 信息缺失导致基础 DTO 无法稳定生成

处理方式：

- 输出差异清单
- 阻止启动
- 不回退到 migration 自动改库

### 8.4 模块迁移流程

1. 为模块补齐全部 `model.json` 文件。
2. 为模块补 `contract` 语义缺口。
3. 增加模块级 metadata provider。
4. 编写“新 provider 投影结果 == 旧手写定义”的等价测试。
5. 生成基础 DTO 与签名清单。
6. 删除对应手写 `ModelDefinition` 与手写基础 DTO。
7. 将模块正式纳入 catalog 聚合与开发态自动结构同步。

## 9. 接口与集成点

### 9.1 Host 接入点

- `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`
  - 增加开发环境分支判断
  - 在开发模式下调用新的结构同步协调器
- `apps/host-api/appsettings.Development.json`
  - 增加 `DevelopmentSchemaSync:Enabled`
- `apps/host-api/HostApi.csproj`
  - 将 `modules/**/metadata/models/*.model.json` 作为运行时内容复制到输出目录

### 9.2 结构同步内核

对现有 `server/tools/dev-schema-sync/` 做拆分：

- 保留 CLI 外壳项目：`server/tools/dev-schema-sync/`
- 抽出共享内核库，例如：
  - `server/platform-persistence-devsync/`

共享内核负责：

- 真源扫描
- 一致性检查
- schema 读取
- diff 计划生成
- 风险判断
- SQL preview
- 自动执行

CLI 与 Host 共用同一套共享内核，避免两条逻辑分叉。

### 9.3 模型 provider 接入点

建议每个模块在 `server/modules/<ModuleId>/Persistence/Descriptors/` 下保留一个轻量 provider 文件，例如：

- `AuthorizationModelMetadataProvider.cs`
- `ConfigCenterModelMetadataProvider.cs`
- `OrganizationModelMetadataProvider.cs`

provider 只负责声明：

- 模块 ID
- 元数据目录
- 共享 loader 的调用

### 9.4 基础 DTO 同步工具

增强 `server/tools/model-dto-sync/`：

- 从单纯演示入口改为真实同步工具
- 输入：模块 `model.json`
- 输出：
  - `Generated/*.g.cs`
  - DTO 签名 provider

### 9.5 迁移与正式链路接入点

- `server/platform-persistence-efcore.migrations.*`
  - 继续通过 `PlatformPersistenceModelCatalog` 读取模型定义
  - 但此时 catalog 的内容已经来自 metadata provider，而非手写定义

## 10. 数据与状态

### 10.1 真源数据

- `modules/<ModuleId>/metadata/models/*.model.json`
  - 唯一 authoring 真源

### 10.2 运行时结果视图

- `ModelDefinition`
- `PlatformPersistenceModelCatalog`
- EF Core model

这些对象都是结果视图，不应被手工编辑来回写真源。

### 10.3 生成代码

- `server/modules/<ModuleId>/DTOs/Generated/*.g.cs`
- `server/modules/<ModuleId>/DTOs/Generated/*SignatureProvider.g.cs`

这些文件是由真源投影出来的可提交结果，应当被纳入版本控制，但不应作为手工维护入口。

### 10.4 临时执行状态

- `SchemaPlan`
- `SQL Preview`
- 单次事务执行结果

这些都是运行时临时状态，不落库，不写 `__EFMigrationsHistory`。

## 11. 错误处理与风险

### 11.1 主要失败点

- `model.json` 缺失或格式非法
- `contract` 语义不足以支撑基础 DTO 投影
- catalog 仍残留手写定义输入
- SqlServer schema 读取失败
- 结构计划中出现高风险动作
- 自动执行 SQL 中途失败

### 11.2 风险控制

- 开发启动前先做一致性检查，再做结构同步
- 只自动执行 `Safe` 级动作
- SQL 执行必须在单事务内完成
- 不允许高风险动作自动放行
- DTO 签名必须参与一致性校验，防止 `model.json` 改了但基础 DTO 未同步

### 11.3 回滚与降级

- 临时关闭 `DevelopmentSchemaSync:Enabled`，回到现有 migration 启动链路
- 模块级迁移失败时，可回退该模块的 provider 切换，但必须同时恢复对应等价测试与文档记录
- 若基础 DTO 生成链路不稳定，可暂停 DTO 自动化，但不能影响 `model.json` 作为模型真源的口径

## 12. 验证方案

### 12.1 设计校验

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\platform\model-truth-development-schema-sync-design.md
```

### 12.2 模型真源与投影等价验证

- 为每个模块增加：
  - 新 provider 投影结果与旧手写定义逐字段、逐索引对比测试

### 12.3 DTO 同步验证

- 为每个基础 DTO 增加：
  - 生成结果快照测试
  - DTO 签名一致性测试

### 12.4 持久化与结构同步验证

- `dotnet build ICP-Platform.sln -v minimal`
- `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj -v minimal`
- `dotnet test tests/Modeling.Tests/Modeling.Tests.csproj -v minimal`
- `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj -v minimal`
- `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj -v minimal`
- `dotnet test tests/Organization.Tests/Organization.Tests.csproj -v minimal`

### 12.5 开发启动 smoke

- 在 `Development + SqlServer` 下：
  - 空计划时正常启动
  - 仅 `Safe` 操作时自动执行后正常启动
  - `Confirm` 操作时阻止启动
  - DTO 签名不一致时阻止启动

## 13. 未决问题

当前无未决问题；本设计已明确：

- `model.json` 是唯一真源
- `PlatformPersistenceModelCatalog` 只聚合 provider 结果
- 基础 DTO 一起收口，但只覆盖模型同构 DTO
- 开发环境自动结构同步只支持 `SqlServer`
- 高风险/失败/不一致都阻止启动

后续若要扩大到请求 DTO、聚合 DTO、插件模型或非 SqlServer provider，需另起设计。

## 14. 实施拆分建议

### 14.1 批次一：补齐模型真源

最小交付：

- 为以下模型补齐 `model.json`
  - `ValueListItem`
  - `Role`
  - `Permission`
  - `RolePermission`
  - `UserRole`
  - `Company`
  - `Department`
  - `Position`
  - `PersonUserLink`
- 为 `Person`、`Position` 等字段补齐 `contract` 语义

### 14.2 批次二：模块 provider 切换

最小交付：

- 模块 provider 全部改为 metadata loader 投影
- 增加等价测试
- 删除对应手写 `ModelDefinition`

### 14.3 批次三：基础 DTO 同步

最小交付：

- `model-dto-sync` 具备真实生成能力
- 受管基础 DTO 改为 `Generated/*.g.cs`
- DTO 签名 provider 生效

### 14.4 批次四：开发启动自动结构同步

最小交付：

- Host 能在 `Development + SqlServer` 下调用共享同步内核
- `Safe` 自动执行
- `Confirm` 阻止启动
- 不写 migration 历史

### 14.5 批次五：正式链路回归与文档收口

最小交付：

- migration 生成仍可使用 catalog
- 开发/正式两条链路的边界文档补齐
- 旧定义清单完全清空

## 15. AI-First 最小检查清单

1. 入口在哪里  
   入口从 `modules/<ModuleId>/metadata/models/*.model.json`、`apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`、`server/tools/dev-schema-sync/`、`server/tools/model-dto-sync/` 开始。

2. 边界在哪里  
   `model.json` 只做声明性 authoring；provider/catalog/DTO 生成/结构同步都是结果视图与代码内核；本轮只允许修改模型真源、provider、catalog、结构同步和基础 DTO 相关文件，不触碰模块私有业务流程。

3. 如何验证  
   通过 AI-first 设计校验脚本、模块投影等价测试、DTO 签名测试、DevSchemaSync 测试、后端构建与开发启动 smoke 验证。

4. 失败信号是什么  
   缺失 `model.json`、provider 投影不一致、DTO 签名不一致、高风险结构计划、SQL 执行失败、相关测试失败，均表示设计未按预期落地。

5. 如何回滚  
   可关闭 `DevelopmentSchemaSync:Enabled` 回到 migration 启动链路；模块级可恢复旧 provider 切换；DTO 自动化若不稳定可单独停用，但不得改变 `model.json` 的真源定位。
