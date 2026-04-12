# ConfigCenter 详细设计

## 1. 背景

`ConfigCenter` 目前已经有后端模块、前端页面和测试，但之前的文档主要回答“边界是什么”和“已经做了什么”，还没有把配置模型、环境/租户覆盖方式、配置同步入口、与其他模块的衔接讲成一份详细设计。

这份设计的目的，是把新平台 V1 的统一配置承载方案稳定下来，让后续模块知道：配置值应如何承载、如何同步默认配置、如何按环境和租户读取，以及为什么当前只做“最小配置中心”而不是“大而全配置平台”。

## 2. 目标

- 提供统一的配置值承载模型和基础 CRUD 能力。
- 支撑按 `module / group / environment / tenant` 组织配置项。
- 提供统一的 `ConfigItem` 值层承载、查询和修改入口。
- 提供前端配置管理页，支撑基础查看和修改。
- 为后续 `Setting` 值管理、审计联动和持久化增强预留空间。

## 3. 非目标

- 不构建复杂审批发布、灰度投放、版本化回滚平台。
- 不一次性迁移旧平台全部元数据资产。
- 不在本轮实现复杂缓存、多级配置源和配置变更推送。
- 不把所有业务元数据都纳入 ConfigCenter；尤其不把 `Dictionary / Enumeration` 的定义层重新拉回数据库权威仓库。

## 4. 范围

涉及范围：

- 后端模块：`server/modules/ConfigCenter/`
- 前端模块：`frontend/modules/config-center/`
- Shell 模块注册：`frontend/packages/shell/src/registerModules.ts`
- 与宿主的配置 schema 装配点

本轮不覆盖：

- 独立字典中心、枚举中心
- 配置迁移批处理工具
- 审批发布和审计中心实现细节

## 5. 设计约束

- 设计必须保持“最小可运行”，不把 ConfigCenter 做成平台中的平台。
- 配置项模型必须兼容未来环境级和租户级覆盖。
- 模块可以声明 `ConfigDescriptor` 作为值层默认项参考，但 `ConfigCenter` 本身不承担 `SettingDefinition` 定义层编辑职责。
- 当前实现允许内存仓储，但服务和 DTO 层要为后续数据库化保留稳定接口。
- 敏感配置必须在查询响应中默认脱敏。

### 5.1 AI-first 最小检查补充

- 入口在哪里：`ConfigCenter` 的正式后端入口是 `server/modules/ConfigCenter/`，对外接口入口是 `GET/POST/PUT/DELETE /api/config` 及其子路由；`Setting` 值层继续通过这里承载。
- 边界在哪里：`ConfigCenter` 负责 `Setting` 值层的统一承载、查询与最小导入能力，不负责完整 i18n 平台、复杂审批发布、全量旧元数据收编，也不再承担独立元数据配置中心的职责。
- 如何验证：通过 `tests/ConfigCenter.Tests`、后端构建、分组查询与敏感字段脱敏验证；当承接 `WP4` 首批迁移时，再叠加 Stage 8/9/10 的定向导入验证。
- 失败信号是什么：如果配置项无法用现有 `ConfigItem` 稳定表达、模块边界被迫扩成新的大模型设计、或新增导入路径无法保持与 `Setting` POC 同一模板，则说明当前方案不再适合继续承载。
- 如何回滚：若后续发现 `WP4` 首批迁移不适合继续共用 `ConfigCenter`，应只回退对应迁移设计与实现，不回退 `ConfigCenter` 已稳定的配置中心主干能力。

## 6. 总体方案

采用“统一 `ConfigItem` 值层 CRUD + 旧 `.setting` 值层导入”的方案：

- `ConfigCenterModule` 仍可通过 `IConfigProvider` 暴露本模块自己的默认配置描述，但这不是定义层编辑入口。
- `ConfigService` 负责 `ConfigItem` CRUD、按模块查询、按组聚合、环境/租户过滤。
- `LegacySettingImportService` 只把旧 `.setting` 数据导入到 `ConfigItem` 值层，不回写或维护 `SettingDefinition` 文件结构。
- 前端模块 `frontend/modules/config-center` 提供配置管理页面，Shell 通过模块注册接入菜单和路由。

选择这个方案的原因：

- 能够先统一值层配置模型，再逐步承接 `Setting` 值导入和更复杂治理。
- 保留轻量模块描述符即可兼容默认值来源，同时避免把定义层编辑继续堆回 ConfigCenter。
- 环境和租户维度以字段方式进入模型，足够支撑 V1。

本轮不选这些方案：

- 不选完全依赖 `appsettings` 或散落 JSON 文件：模块配置会继续分裂。
- 不选先做独立配置发布平台：会把范围拉大太多。
- 不选先把旧平台元数据体系整体搬过来：迁移风险和成本过高。

## 7. 关键模型

### 配置定义参考模型

- `SettingDefinition / ConfigDescriptor`
  - `Key`
  - `DisplayName`
  - `Description`
  - `Type`
  - `DefaultValue`
  - `Required`
  - `ModuleId`
  - `Group`

### 运行时配置值模型

- `ConfigItem`
  - `Id`
  - `Key`
  - `Value`
  - `Type`
  - `ModuleId`
  - `Group`
  - `Environment`
  - `TenantId`
  - `DisplayName`
  - `Description`
  - `IsSensitive`
  - `CreatedAt`
  - `UpdatedAt`
  - `UpdatedBy`

### DTO

- `ConfigItemDto`
- `ConfigItemCreateDto`
- `ConfigItemUpdateDto`
- `ConfigGroupDto`

### 前端模型

- `FrontendModule.routes`
- `FrontendModule.menus`
- 配置管理页中的列表、编辑表单和分组视图状态

## 8. 核心流程

### 主流程：读取配置

1. 调用方通过 `GetAllConfigsAsync`、`GetConfigAsync` 或 `GetConfigsByModuleAsync` 发起查询。
2. 服务层从仓储读取配置项。
3. 若传入 `environment / tenantId`，按“精确匹配或基础配置兜底”的当前规则过滤。
4. 返回 DTO；若配置项标记为敏感，则 `Value` 返回 `"******"`。

### 主流程：修改配置

1. 前端或调用方提交创建/更新请求。
2. 服务层校验目标项是否存在。
3. 对允许更新的字段进行修改，并记录 `UpdatedAt / UpdatedBy`。
4. 仓储保存后返回 DTO。

### 异常流程

- 目标配置项不存在：更新时抛出业务异常，控制器返回 404。
- 敏感配置被读取：值返回脱敏结果，而不是原始明文。
- 若模块还未初始化对应 `ConfigItem`：调用方需要通过自己的初始化或导入流程补齐值层数据。

## 9. 接口与集成点

### 后端接口

- `GET /api/config`
- `GET /api/config/grouped`
- `GET /api/config/{key}`
- `GET /api/config/module/{moduleId}`
- `POST /api/config`
- `PUT /api/config/{id}`
- `DELETE /api/config/{id}`

### 与宿主/模块的集成

- `ConfigCenterModule` 本身实现 `IConfigProvider`、`IMenuProvider`、`IPermissionProvider`。
- 其他模块可以读取配置，但不应各自实现孤立的配置存储。
- 若模块需要维护 `SettingDefinition`，应通过独立的 `MetadataConfigCenter` 维护定义层文件。
- `MetadataConfigCenter` foundation 闭环已验证：定义层读取/保存走文件 authoring 链路，`ConfigCenter` 不参与 `SettingDefinition` 文件编辑。

### 与前端 Shell 的集成

- `frontend/modules/config-center/src/index.ts` 声明配置中心路由、菜单和权限。
- `frontend/packages/shell/src/registerModules.ts` 注册配置中心模块。
- Shell 权限守卫通过 `ConfigCenter.Config.View` 控制菜单和页面可见性。

## 10. 数据与状态

- 当前仓储实现为进程内存储。
- 基础配置、环境配置、租户配置共用同一张逻辑模型，通过 `Environment` 和 `TenantId` 区分作用域。
- 当前读取规则偏轻量，尚未形成完整优先级解析引擎。
- 敏感配置在 API 返回层统一脱敏，但原始值仍保存在仓储中。
- 一致性要求：值层数据、前端权限配置和管理页面能力要保持同步，否则会出现“页面可见但配置项不存在”的情况。

## 11. 错误处理与风险

- 当前还没有类型级校验，`Type` 主要是描述信息，值仍以字符串保存。
- 还没有审计联动，配置变更历史不可追踪。
- 环境和租户过滤是轻量实现，后续若出现更复杂覆盖规则，需要重新定义优先级模型。
- 目前为内存仓储，重启后需要依赖同步和初始化逻辑恢复基础配置。

风险控制方式：

- 用 `ConfigItem` 契约约束键名、作用域和敏感字段行为。
- 对敏感配置默认脱敏，避免前端列表直出。
- 未来持久化、导入、审计能力都通过服务层继续演进，不直接破坏现有接口。

## 12. 验证方案

- `tests/ConfigCenter.Tests` 通过。
- 后端构建通过，前端构建通过。
- 能通过 API 创建、查询、更新、删除配置项。
- 敏感配置返回值被脱敏。
- 配置中心页面可在有权限时打开，并能看到基础配置列表。
- 元数据配置中心 foundation 收口时，`ConfigServiceTests` 与 `LegacySettingImportServiceTests` 已再次验证 `ConfigCenter` 仍只承载 `ConfigItem` 值层，不承担 `SettingDefinition` 定义编辑职责。

## 13. 未决问题

- 是否需要在下一阶段引入更明确的配置优先级规则，例如“tenant > environment > global”。
- 配置变更是否在下一阶段接入 Audit 模块。

### 13.1 阶段性决定与保留问题

- `WP4` 首批 `Dictionary / Enumeration` 不再默认把 `ConfigCenter` 作为权威多语言数据仓库。
- `ValueListItem` 仍不作为本轮 `Dictionary / Enumeration` 首批导入目标。
- `Setting` 继续属于元数据体系，但其定义层不等于值层：定义由模块元数据承载，值由 `ConfigCenter` 承载。
- `ConfigCenter` 继续负责 `ConfigItem` 值层承载；若未来需要消费 `Dictionary / Enumeration`，应优先通过静态资源或静态清单的受控投影接入。
- `Dictionary` 更适合长期演进为静态 i18n 资源；`Enumeration` 更适合长期演进为静态结构清单 + 本地化资源，而不是数据库权威源。

## 14. 实施拆分建议

1. 固化 `ConfigItem` 值层契约与最小导入逻辑。
2. 固化配置项领域模型、仓储、服务和控制器。
3. 固化前端配置中心页面与菜单接入。
4. 补类型校验、持久化与审计联动。
5. 再推进旧配置资产导入 POC，而不是提前把全部迁移复杂度压进当前实现。
