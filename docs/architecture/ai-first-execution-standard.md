# AI-First 执行标准

## 1. 目的

本文档是 `NewP` 仓库中 AI-first 执行层的正式入口，用来把已经分散在 `AGENTS.md`、`docs/architecture/ai-first-platform-criteria.md`、`docs/architecture/componentization-guardrails.md` 与设计模板中的规则收成一套可直接执行的日常标准。

它不替代上述源文档，而是提供统一引用口径和最小执行闭环。

## 2. 适用范围

默认适用于当前仓库中所有新增的：

- 任务拆分与工作包
- 详细设计文档
- 后端模块与前端模块实现
- 迁移链路、导入规则与验证记录

既有内容不要求一次性返工，但后续修改时应逐步对齐。

## 3. 核心执行规则

### 3.1 单任务单目标

- 一次任务默认只聚焦一个模块或一种能力。
- 不把重构、新功能、迁移混在同一次改动里。
- 如果当前需求天然跨多个子方向，先拆包，再逐步落地。

### 3.2 契约与设计先行

- 没有详细设计文档时，不进入正式实现。
- 正常模块接入优先通过 `Contracts / SDK / templates / controlled entry points`。
- 如果现有契约或受控入口不够用，应先补设计说明，不临场扩口。

### 3.3 文档必须可执行

- 文档不仅要写“做了什么”，还要写“从哪里入手、允许改哪里、如何验证、失败怎么看、如何回滚”。
- 如果某项实现依赖口口相传才能继续推进，则视为不符合 AI-first。
- 文档不应依赖单机路径、单机用户名或特定 IDE 会话状态。

### 3.4 验证必须局部且可复跑

- 每项改动都应优先选择局部的 build / test / smoke / 脚本校验。
- 不能把“靠人工点击一遍”作为主要验证方式。
- 如果只能依赖整仓联调才能确认结果，应先继续拆小。

### 3.5 并行必须可控

- 子智能体或多人协同时，必须给出清晰的文件边界。
- 不同执行单元优先落在不同文件集合，避免共享隐式现场状态。
- 主线程负责关键取舍、冲突判断与最终复核。

### 3.6 提交信息默认中文

- 默认使用简体中文提交信息，除非用户或仓库规则明确要求使用其他语言。
- 提交前缀也默认使用简体中文，推荐使用：`文档：`、`设计：`、`功能：`、`修复：`、`重构：`、`测试：`、`验证：`。
- 一条提交信息默认只表达一个主要意图，避免把多个不相关目标塞进同一次提交说明中。
- 计划、设计和验证文档中的提交示例也应优先使用简体中文，避免执行阶段被英文示例带偏。

## 4. 统一判断口径

以下 8 项作为日常执行与验收时的统一判断口径：

1. 边界清晰
2. 任务可切小
3. 局部可验证
4. 契约先于实现
5. 低上下文负担
6. 文档可执行
7. 并行安全
8. 可复制迁移

完整解释见 [ai-first-platform-criteria.md](ai-first-platform-criteria.md)。

## 5. 设计文档最小检查清单

每份新设计文档至少回答：

1. 入口在哪里
2. 边界在哪里
3. 如何验证
4. 失败信号是什么
5. 如何回滚

如果当前阶段确实无法回答其中某项，必须显式写明原因，而不是留空或依赖口头补充。

## 6. 受控入口与边界

- Host 只负责启动、组合、模块加载、统一认证与宿主级能力。
- Shell 只负责布局、全局 Provider、菜单聚合、路由聚合和模块受控注册。
- 正常前端模块接入时，唯一允许修改的 Shell 级入口仍是 `frontend/packages/shell/src/builtin-runtime-plugins.ts`。
- 公共能力进入 `Kernel / SDK / Contracts`，模块私有能力留在模块内部。

详细边界见 [componentization-guardrails.md](componentization-guardrails.md)。

## 7. 当前最小执行闭环

本仓库当前已落地的 AI-first 执行闭环入口如下：

1. 正式标准入口：本文档
2. 设计默认入口：[../superpowers/specs/](../superpowers/specs/)
3. 设计模板入口：[../designs/detailed-design-template.md](../designs/detailed-design-template.md)
4. 受控目标清单：`docs/architecture/ai-first-design-check-targets.txt`
5. 定向校验入口：`scripts/validate-ai-first-design.ps1`
6. 受控门禁入口：`scripts/check-ai-first-design-targets.ps1`
7. 目标登记入口：`scripts/register-ai-first-design-target.ps1`
8. 设计创建入口：`scripts/new-design-doc.ps1`（默认创建到 `docs/superpowers/specs/`，支持 `-Category platform|modules|tooling|roadmap` 分类落位，文件名自动带 `YYYY-MM-DD-` 前缀，创建前会先校验模板是否满足 AI-first 最小要求）
9. 仓库协作规则入口：[../../AGENTS.md](../../AGENTS.md)
10. 阶段拆包与验收入口：[../phases/post-phase-6-stage-map.md](../phases/post-phase-6-stage-map.md)

示例命令：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\detailed-design-template.md
```

校验当前受控基础集：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

对某份新增设计文档做提交前校验：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\<your-design>.md
```

如果这份设计文档需要进入显式目标清单：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\register-ai-first-design-target.ps1 -DesignPath .\docs\superpowers\specs\<your-design>.md
```

如果需要直接按模板创建并登记一份新设计文档：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\new-design-doc.ps1 -DesignName <your-design-name>
```

当前推荐的默认执行顺序是：

1. 用 `new-design-doc.ps1` / `new-design-doc.sh` 创建设计文档
2. 用 `validate-ai-first-design.ps1` 做提交前校验
3. 用 `check-ai-first-design-targets.ps1` 做受控目标集校验
4. 在协作工作包或阶段验收中显式引用本文档作为 AI-first 判断口径

## 8. 当前未纳入首轮闭环的内容

本轮刻意不做以下扩展，以避免把 WP2 提前扩散成 WP3：

- 不对现有全部设计文档做一次性回填
- 不把 AI-first 校验强绑到整仓所有文档
- 不在本轮修改已退场的历史路线文档
- 不把状态一致性治理与全量文档补链并入本轮

## 9. 对应源文档

- 全局规则：[../../AGENTS.md](../../AGENTS.md)
- 判定准则：[ai-first-platform-criteria.md](ai-first-platform-criteria.md)
- 组件化护栏：[componentization-guardrails.md](componentization-guardrails.md)
- 设计模板：[../designs/detailed-design-template.md](../designs/detailed-design-template.md)
- WP2 设计依据：[../designs/ai-first-execution-standard-design.md](../designs/ai-first-execution-standard-design.md)
