# AGENTS.md

## 项目定位

本仓库是平台基础能力与扩展内核仓库，不是某个单一业务应用仓库。

平台提供可复用的 Host、契约、SDK，以及内建公共模块，供产品线和项目组在其上继续构建。

## 主要目标

- 保持模块边界清晰。
- 优先通过扩展契约集成，而不是直接耦合。
- 将平台内核与产品业务逻辑分离。
- 将迁移视为代码、元数据、数据、宿主与工具的整体问题。
- 所有改动都应尽量可验证、可回退。
- 将“更适合 AI 辅助开发并逐步过渡到 AI 主导开发”作为首要目标，而不是单纯追求功能堆叠或旧平台等比例重做。

## AI-First 约束

- 任何设计都要优先判断：AI 是否能在小上下文内理解、修改、验证和回滚。
- 单个任务应尽量保持单一目标，避免把重构、新功能、迁移混在一起。
- 新增模块、迁移链路和扩展能力要优先形成可复制模板，方便后续 agent 复用。
- 文档不仅要解释结果，还要说明入口、边界、验证方式、失败信号和回滚思路。
- 如果某项实现只能依赖人工口口相传才能继续推进，则说明它还不符合当前项目目标。

## 架构规则

- Host 只负责启动、组合、认证接入、路由/菜单聚合和模块加载。
- Kernel 提供共享抽象与基础设施能力。
- 内建模块依赖共享契约和 Kernel 抽象，不依赖其他模块的具体基础设施实现。
- 新的扩展能力必须通过模块契约与 SDK 暴露。
- 不允许跨模块直接访问对方数据库或持久化细节。
- 不要把产品专属页面或流程直接放进平台 Shell。
- 不要让迁移期的临时方案固化成长期平台架构。

## 迁移规则

- 先盘点，再实施。
- 每一类旧资产都必须先被归类为：`Retire / Rewrite / Wrap / Import / Postpone` 之一。
- 不要隐式迁移旧运行时元数据，必须先文档化其目标形态。
- 未经契约审查，不要把旧框架启动代码直接搬进新 Host。
- 任何临时兼容层都要保留明确清单。
- 除非当前阶段明确纳入，否则 `WinApp`、`Studio`、`File Service` 重写默认都视为范围外。

## 后端规则

- 新平台使用当前 LTS 的 .NET 目标版本。
- 所有服务组合都通过依赖注入完成。
- Controller 保持轻薄，业务逻辑应落在应用服务或领域服务中。
- 对外暴露稳定 DTO 契约。
- 每个新增模块能力都应补单元测试或集成测试。

## 前端规则

- 使用 `React + TypeScript + Vite`。
- Shell 负责布局、认证上下文、菜单组合、路由组合和全局 Provider。
- 共享组件放在 `frontend/packages/ui`。
- 模块前端必须通过前端 SDK 注册，不应直接修改 Shell 内部结构实现接入。
- 除非有充分理由，否则避免引入全局可变状态。
- 任何旧页面或旧元数据兼容路径都必须被隔离并文档化。

## 变更规则

- 一次任务应只聚焦一个模块或一种能力。
- 除非确有必要，不要把重构、重命名和新功能混在同一次改动里。
- 先阅读相关文件，再形成方案，再开始编辑。
- 尽量保持 diff 小而清晰。
- 当契约、架构或迁移决策变化时，必须同步更新文档。
- 任何新的功能、模块、阶段改动或行为变更，在正式实施前都必须先有详细设计文档。
- 如果还没有详细设计文档，工作只能停留在调研、澄清或设计草案阶段，不得直接进入实现。
- 详细设计文档默认放在 `docs/designs/` 下，除非该模块已有批准的专属位置。

## 未经明确请求不得做的改动

- 不要擅自引入微服务拆分。
- 不要随意修改部署脚本、密钥处理、CI 流水线或基础设施编排。
- 不要在没有记录权衡的情况下替换基础库。
- 不要因为旧元数据或旧配置“看起来过时”就静默丢弃它们。

## 验证要求

每次改动都应尽量尝试执行：

1. 后端构建
2. 后端测试
3. 前端构建
4. 前端测试
5. 契约检查或 smoke 验证（如果相关）

## 输出要求

每次完成工作后，都应明确报告：

- 改了什么
- 影响了哪些文件或模块
- 验证结果
- 已知风险
- 回滚思路
## Componentization Guardrails

- New modules should go through `Contracts / SDK / templates / controlled entry points` before changing Host or Shell internals.
- `Host` should only own startup, composition, module loading, and host-level concerns.
- `Shell` should only own layout, global providers, menu aggregation, route aggregation, and controlled registration.
- Normal frontend module integration may update `frontend/packages/shell/src/builtin-runtime-plugins.ts`; `registerModules.ts` should remain focused on shell-side loading/composition logic.
- The full guardrails reference lives in `docs/architecture/componentization-guardrails.md`.

## 提交约定

- 后续新增的 Git 提交信息默认使用简体中文，除非用户明确要求使用其他语言。
- 提交信息前缀默认也使用简体中文，推荐使用：`文档：`、`设计：`、`功能：`、`修复：`、`重构：`、`测试：`、`验证：`。
- 一条提交信息应只表达一个主要意图，优先写清“做了什么”，必要时再补“为什么”。
- 除非仓库内某条已有明确规则要求英文提交，否则不要默认生成英文提交信息。
- 默认生成的设计、计划、说明等项目文档必须使用简体中文，除非用户明确要求使用其他语言。
