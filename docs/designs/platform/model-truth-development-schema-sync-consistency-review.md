# 模型真源与开发态自动结构同步一致性 Review

## 1. Review 范围

本 review 用于满足 `AGENTS.md` 中“详细设计文档或实施计划完成后，在进入正式实施前，必须先做一次一致性 review”的要求。

本次 review 只检查设计、计划与架构护栏是否一致，不进入代码实现，也不把当前仓库尚未完成的实现现状混入结论。

本次 review 以以下文档为范围：

1. `docs/designs/platform/model-truth-development-schema-sync-design.md`
2. `docs/superpowers/plans/2026-04-23-model-truth-development-schema-sync.md`
3. `docs/architecture/componentization-guardrails.md`
4. `AGENTS.md` 中与 AI-first、文档、范围、宿主边界、验证和回滚相关的约束

## 2. 当前阶段基线结论

### 2.1 真源与结果视图基线

- `modules/<ModuleId>/metadata/models/*.model.json` 是唯一 authoring 真源。
- `ModelDefinition`、`PlatformPersistenceModelCatalog`、EF model、基础 DTO 生成产物都只是结果视图，不得反向充当长期真源。
- 所有仍保留的手写 `ModelDefinition` 都属于待退出对象，本轮目标不是与 `model.json` 并存，而是完成替换。

### 2.2 宿主与内核边界基线

- `Host` 只负责环境判断、启动分支、失败中止和宿主级组合。
- 真正的开发态结构同步逻辑必须收口在共享代码内核中，而不是散落在 `Host` 或 CLI 文本流程里。
- `server/tools/dev-schema-sync/` 需要拆出可复用内核，由 CLI 与 Host 共用，避免形成两套分叉逻辑。

### 2.3 DTO 边界基线

- 本轮只把“模型同构基础 DTO”纳入统一同步链路。
- `*Request`、`*DetailDto`、`*SummaryDto`、`*TreeNodeDto` 和其他聚合 DTO 继续手写，不在本轮自动生成范围内。
- 新增的 `contract` 分区只服务 DTO / 契约投影，不参与数据库结构计算。

### 2.4 开发态与正式链路基线

- 自动结构同步只允许在 `Development + SqlServer + DevelopmentSchemaSync:Enabled=true` 下启用。
- 只允许自动执行 `Safe` 级结构操作。
- 任一 `Confirm` 级操作、真源不一致、DTO 签名不一致或 SQL 执行失败，都必须阻止启动。
- 正式环境和 migration 发布链路继续保留，不因本轮设计被替换。

### 2.5 实施顺序基线

- 正确顺序应为：补齐 `model.json` 真源 -> 补 `contract` 语义 -> 增加 provider 与等价测试 -> 切 catalog -> 同步基础 DTO -> 接入开发态自动 schema sync。
- 虽然本轮会触达 `Authorization / Organization / ConfigCenter` 多个模块，但这些改动都服务同一平台能力收口，不是多个无关功能拼盘。
- 计划文档当前引用的仓库内路径均为相对路径，符合 `docs/superpowers/plans/` 的路径约束。

## 3. 一致性结论

### 3.1 与 AGENTS 约束的一致性

当前设计与计划满足以下关键要求：

- 已先形成详细设计，再形成实施计划，没有跳过设计直接实现。
- 设计明确回答了入口、边界、验证方式、失败信号和回滚思路，符合 AI-first 最小检查清单。
- 设计把真源、投影、执行、风险判断和回滚信号分离开来，没有把执行流程继续下沉到元数据。
- 设计与计划都保留了验证路径和回退路径，符合“可验证、可回退”的迁移规则。

### 3.2 与组件化护栏的一致性

当前方案与 `docs/architecture/componentization-guardrails.md` 保持一致：

- 不触碰前端 `Shell` 或模块前端接入机制。
- `Host` 只增加启动判断与组合入口，不承接模块私有业务规则或模块私有数据访问细节。
- 新能力主要落在共享建模、持久化组合与开发态同步内核中，不是为单个模块特制的宿主内嵌逻辑。
- 没有引入跨模块直接访问彼此数据库的设计，也没有借机固化迁移期临时宿主方案。

### 3.3 设计与实施计划的一致性

设计文档与计划文档在以下关键点上保持一致：

- 都把 `model.json` 明确为唯一真源。
- 都要求 `PlatformPersistenceModelCatalog` 只聚合 provider 结果，不再直接依赖手写 `.All` 常量。
- 都把基础 DTO 自动化边界收紧在“模型同构基础 DTO”。
- 都保留正式 migration 链路，并把开发态自动 schema sync 限定在 `Development + SqlServer`。
- 都把高风险结构变更定义为阻断启动，而不是继续放行。
- 都要求先补模块真源和等价测试，再接启动前自动同步。

### 3.4 总体结论

本次 review 的总体结论是：

**当前方案在设计、计划、目录落点、宿主边界、DTO 范围和回滚策略上整体一致，不需要推翻重做；在收口两处计划级偏差后，可以进入正式实施。**

## 4. 当前需要显式收口的偏差项

### 4.1 计划文档的语言约定尚未完全对齐

`docs/superpowers/plans/2026-04-23-model-truth-development-schema-sync.md` 当前仍保留英文标题、英文段落标题和部分英文提示语。

这不构成架构冲突，但与 `AGENTS.md` 中“默认生成的设计、计划、说明等项目文档必须使用简体中文”的约定不完全一致。正式实施前，应先把计划文档的标题和主要说明收口为中文，避免后续 agent 在入口文档上继续扩散混合语体。

### 4.2 计划中的 Host 触点比设计口径略宽

设计文档在宿主接入点上只明确要求：

- `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`
- `apps/host-api/appsettings.Development.json`
- `apps/host-api/HostApi.csproj`

而计划文档的 Task 8 文件清单额外列入了 `apps/host-api/Program.cs`。

这不一定代表实现一定错误，但从当前设计口径看，`Program.cs` 不是默认必改项。正式实施时，应先按最小宿主改动原则推进，优先把逻辑收口在 `PersistenceBootstrapExtensions.cs`；只有当注册顺序或启动入口经过代码验证后确认必须改 `Program.cs`，才应扩大宿主改动面，并同步回写设计与 review 文档。

### 4.3 分批提交时要避免“半新半旧”状态固化

当前计划采用多任务、分批提交推进，这是合理的；但需要显式强调以下纪律：

- 在模块等价测试和 DTO 签名能力未就位前，不应提前打开开发态自动 schema sync 默认行为。
- 在 catalog 尚未完全切到 provider 结果前，不应把“真源已经统一”作为完成结论。
- 在基础 DTO 尚未生成并接入签名校验前，不应删除对应手写基础 DTO。

这不是新的设计冲突，而是正式实施时必须遵守的合入边界，否则会把分阶段实施误落成长期的“半新半旧”结构。

## 5. 阻断项与非阻断项

### 5.1 当前阻断项

从架构与范围一致性的角度看，当前**没有必须推翻方案重做的设计级阻断项**。

从实施前收口的角度看，以下两项应先修正：

1. 计划文档语言口径收口为中文。
2. 计划文档中的宿主触点说明收紧到最小必要范围。

### 5.2 当前非阻断项

以下内容当前都属于明确后置的非阻断项：

- 非 `SqlServer` provider 的开发态自动结构同步
- 插件模型接入本轮自动结构同步
- 基础 DTO 之外的请求 DTO、聚合 DTO、树 DTO、详情 DTO 自动化
- migration 工程的移除或正式发布治理重构

此外，当前仓库运行时仍依赖 migration 三件套属于“现状问题”，并不构成本次设计与计划之间的新冲突；它正是本轮实施要解决的目标。

## 6. 建议的下一步动作

基于本次 review，建议按以下顺序继续推进：

1. 先修正计划文档的中文化与宿主触点描述。
2. 修正完成后，再进入正式实施。
3. 正式实施时保持当前计划的大顺序不变：先真源与 provider，后 DTO，最后再接 Host 开发态自动 schema sync。
4. 在最终接入 `DevelopmentSchemaSync` 之前，不要提前改变开发环境默认启动行为。

## 7. Review 结论摘要

- 设计、计划与平台护栏整体一致。
- `model.json` 真源、provider 聚合、基础 DTO 边界和开发态阻断策略口径一致。
- 当前没有设计级回退阻断。
- 正式实施前，需要先收口计划文档语言和宿主触点两项计划级偏差。
