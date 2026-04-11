# GitHub Copilot 协作模式

本文档是 `NewP` 仓库里专门给 GitHub Copilot 使用的固定总说明。

目标不是让 Copilot 自己决定路线，而是让它在清晰边界内执行单个工作包，由 Codex 负责拆包、审查、复核和收口。

## 1. 适用方式

推荐工作流：

1. 先阅读：
   - `AGENTS.md`
   - `Plan.md`
   - `docs/current-status.md`
   - `docs/architecture/ai-first-execution-standard.md`
   - `docs/designs/README.md`
   - 本文档
   - 当前工作包文档
2. 先确认 `docs/collaboration/post-phase-6-copilot-stage-map.md` 中当前所在阶段和下一阶段。
3. 只执行当前工作包允许的范围。
4. 完成后输出改动摘要、验证结果、未完成项和风险。
5. 将结果交回 Codex 审查，不自行宣布“阶段完成”。

推荐给 Copilot 的最短入口指令：

- `请先阅读 docs/collaboration/post-phase-6-copilot-stage-map.md，再按 docs/collaboration/work-packages/<wp-file>.md 执行，并遵守 docs/collaboration/github-copilot-working-mode.md。`

## 2. 角色分工

### GitHub Copilot 负责

- 按工作包执行单个明确任务。
- 只修改工作包允许的文件。
- 按工作包中的验证方式自检。
- 明确汇报改动、验证、风险和剩余问题。

### Codex 负责

- 判断下一阶段做什么。
- 拆分工作包。
- 控制范围和边界。
- 审查 Copilot 产出。
- 运行必要的补充验证。
- 决定是否需要返修或进入下一包。

## 3. 全局规则

- 当前默认假设：GitHub Copilot 使用 `Claude Opus 4.6` 模型。
- 当前默认假设：GitHub Copilot 可使用 `askQuestions` 工具。
- 默认只处理当前工作包，不顺手扩范围。
- 没有明确设计时，不进入正式实现。
- 正常接入优先通过 `Contracts / SDK / templates / controlled entry points`。
- 如果当前工作包需要新增设计文档，优先使用 `scripts/new-design-doc.ps1` / `scripts/new-design-doc.sh` 创建，而不是手工复制旧文档。
- 如果当前工作包修改了设计文档，交回前至少运行一次 `scripts/validate-ai-first-design.ps1`；如已进入 AI-first 目标集，再运行一次 `scripts/check-ai-first-design-targets.ps1`。
- 不把架构判断、状态口径和阶段结论交给 Copilot 单独决定。
- 不把历史文档里的旧阶段口径直接覆盖到当前主文档。
- 不写依赖单机路径、单机用户名、单机 IDE 状态的内容。
- 文档默认要能推送到 GitHub，并在另一台机器上直接复用。

### `askQuestions` 使用约束

- `askQuestions` 只用于补齐当前工作包真正缺失、且无法从仓库现有文档直接获得的信息。
- 优先先读仓库内文档，再决定是否提问，不要把已在文档中的信息再问一遍。
- 一次只问最小必要问题，避免把单个工作包拖成长链路追问。
- 如果已经能在保守边界内继续执行，应优先执行并明确记录假设，而不是反复追问。
- 如果用户通过 `askQuestions` 给出了关键约束，后续输出中应显式说明这些约束如何影响本次改动。

## 4. 文件边界

### 默认不应由 Copilot 主动改动

- `Plan.md`
- `docs/current-status.md`
- `AGENTS.md`
- `CLAUDE.md`
- `docs/designs/*`

例外：

- 只有当前工作包明确允许时，Copilot 才能修改上述文件。

### 适合 Copilot 改动的内容

- 当前工作包明确列出的文档
- 当前工作包明确列出的实现文件
- 当前工作包明确列出的测试文件
- 当前工作包明确列出的说明补充

## 5. 输出要求

每次执行结束后，Copilot 的回复至少应包含：

- 改了什么
- 修改了哪些文件
- 跑了哪些验证
- 哪些验证没有运行
- 已知风险或不确定点
- 是否需要 Codex 继续审查或返修

如果工作包要求“只做文档”：

- 不要顺手补代码实现
- 不要顺手修改计划状态
- 不要把建议写成既成事实
- 如果本轮涉及新增设计文档，仍应按 AI-first 标准走创建、校验和必要的门禁检查

如果工作包要求“只做局部实现”：

- 不要顺手清理无关旧代码
- 不要跨模块扩散改动
- 不要把重构、新功能和文档重写混在一起

## 6. 跨机器约束

本文档默认面向“仓库会被推送到 GitHub，并在不同机器上继续工作”的使用场景。

因此：

- 一律优先使用相对路径。
- 不引用本机临时目录、进程号文件或本地缓存路径。
- 不假设固定操作系统、固定终端或固定 IDE 会话状态。
- 如果某条验证依赖本机环境，必须写出依赖前提。

## 7. 工作包格式

每个工作包应至少包含：

- 目标
- 当前阶段归属
- 必读上下文
- 允许修改的文件
- 禁止触碰的范围
- 执行要求
- 验收标准
- 建议验证方式
- 交回 Codex 时必须汇报的内容

如果工作包涉及新增设计文档或阶段验收文档，还应显式引用：

- `docs/architecture/ai-first-execution-standard.md`
- `docs/designs/README.md`

## 8. 当前推荐用法

如果你准备让 Copilot 执行当前阶段任务，优先从这里开始：

1. `docs/collaboration/github-copilot-working-mode.md`
2. `docs/architecture/ai-first-execution-standard.md`
3. `docs/collaboration/post-phase-6-copilot-stage-map.md`
4. 当前阶段对应的 `docs/collaboration/work-packages/<wp-file>.md`
5. `docs/collaboration/codex-review-checklist.md`

当前建议先执行的首个工作包是：

- 以 `docs/collaboration/post-phase-6-copilot-stage-map.md` 中当前推荐阶段为准
